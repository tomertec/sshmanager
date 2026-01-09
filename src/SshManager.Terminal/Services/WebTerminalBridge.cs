using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Wpf;

namespace SshManager.Terminal.Services;

/// <summary>
/// Bridges communication between C# and the WebView2 xterm.js terminal.
/// Handles bidirectional message passing using WebView2's PostWebMessageAsJson
/// and WebMessageReceived events.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists:</b> WebView2 hosts the xterm.js terminal in a separate process.
/// Communication happens via JSON messages, which has significant overhead. This bridge
/// optimizes performance through:
/// </para>
/// <list type="bullet">
/// <item><b>Write batching:</b> Accumulates writes for 8ms before sending to reduce message count</item>
/// <item><b>Data buffering:</b> Buffers incoming SSH data until xterm.js signals "ready"</item>
/// <item><b>UI thread dispatch:</b> Automatically marshals calls to the UI thread for WebView2</item>
/// </list>
/// <para>
/// <b>Message Protocol:</b> Uses a simple JSON protocol with "type" field:
/// <code>
/// C# → JS: write, resize, setTheme, setFont, focus, clear, fit
/// JS → C#: input, ready, resized
/// </code>
/// </para>
/// </remarks>
public sealed class WebTerminalBridge : IDisposable
{
    private readonly ILogger<WebTerminalBridge> _logger;

    // Pre-ready buffering: SSH data may arrive before xterm.js is initialized.
    // We buffer it here and flush when the "ready" message arrives.
    private readonly object _bufferLock = new();
    private readonly List<string> _pendingData = new();

    private WebView2? _webView;
    private bool _disposed;
    private bool _isReady;
    private int _columns;
    private int _rows;
    private double _fontSize = DefaultFontSize;

    // PERFORMANCE OPTIMIZATION: Write batching
    // Problem: Each PostWebMessageAsJson call has ~1-2ms overhead due to cross-process marshaling.
    // At high data rates (e.g., `cat largefile.txt`), this causes severe slowdowns.
    // Solution: Accumulate writes in a StringBuilder and flush periodically.
    // The 8ms delay (~120fps) is imperceptible while dramatically reducing message count.
    private readonly object _writeBatchLock = new();
    private readonly System.Text.StringBuilder _writeBatch = new();
    private System.Threading.Timer? _writeBatchTimer;
    private int _timerRunning = 0; // Thread-safe flag for timer state
    private const int WriteBatchDelayMs = 8; // ~120fps, imperceptible delay

    /// <summary>
    /// Default font size for the terminal.
    /// </summary>
    public const double DefaultFontSize = 14;

    /// <summary>
    /// Minimum font size for zoom.
    /// </summary>
    public const double MinFontSize = 8;

    /// <summary>
    /// Maximum font size for zoom.
    /// </summary>
    public const double MaxFontSize = 32;

    /// <summary>
    /// Font size increment/decrement step for zoom.
    /// </summary>
    public const double FontSizeStep = 1;

    /// <summary>
    /// Gets the WebView2 control associated with this bridge.
    /// </summary>
    public WebView2? WebView => _webView;

    /// <summary>
    /// Gets whether the terminal is ready to receive commands.
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// Gets the current number of columns in the terminal.
    /// </summary>
    public int Columns => _columns;

    /// <summary>
    /// Gets the current number of rows in the terminal.
    /// </summary>
    public int Rows => _rows;

    /// <summary>
    /// Gets the current font size.
    /// </summary>
    public double FontSize => _fontSize;

    /// <summary>
    /// Event raised when user types in the terminal.
    /// The string contains the user input text.
    /// </summary>
    public event Action<string>? InputReceived;

    /// <summary>
    /// Event raised when the terminal is initialized and ready.
    /// </summary>
    public event Action? TerminalReady;

    /// <summary>
    /// Event raised when the terminal is resized.
    /// Parameters are (columns, rows).
    /// </summary>
    public event Action<int, int>? TerminalResized;

    /// <summary>
    /// Event raised when data is written to the terminal.
    /// Used for capturing output preview for tab tooltips.
    /// </summary>
    public event Action<string>? DataWritten;

    // Buffer for output preview - stores last ~200 characters of plain text output
    private readonly object _previewBufferLock = new();
    private readonly System.Text.StringBuilder _outputPreviewBuffer = new();
    private const int MaxPreviewLength = 200;

    public WebTerminalBridge(ILogger<WebTerminalBridge>? logger = null)
    {
        _logger = logger ?? NullLogger<WebTerminalBridge>.Instance;
    }

    /// <summary>
    /// Initializes the bridge with a WebView2 control and sets up message handling.
    /// </summary>
    /// <param name="webView">The WebView2 control to bridge with.</param>
    public Task InitializeAsync(WebView2 webView)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebTerminalBridge));
        }

        if (webView == null)
        {
            throw new ArgumentNullException(nameof(webView));
        }

        if (_webView != null)
        {
            _logger.LogWarning("InitializeAsync called but WebView already set");
            return Task.CompletedTask;
        }

        _webView = webView;
        _webView.WebMessageReceived += OnWebMessageReceived;
        _logger.LogDebug("WebTerminalBridge initialized with WebView2 control");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends data to the terminal for display.
    /// Buffers data if terminal is not ready yet.
    /// Uses batching to reduce WebView2 message overhead for better performance.
    /// </summary>
    /// <param name="data">The text data to write to the terminal.</param>
    /// <remarks>
    /// <para>
    /// <b>Data flow:</b> SSH server → SshTerminalBridge → (here) → xterm.js
    /// </para>
    /// <para>
    /// <b>Timing scenarios:</b>
    /// <list type="number">
    /// <item>Terminal not ready: Data buffered in _pendingData, flushed when "ready" received</item>
    /// <item>Terminal ready, no pending batch: Data added to batch, timer started</item>
    /// <item>Terminal ready, batch pending: Data appended to existing batch</item>
    /// <item>Timer fires: Batch flushed to WebView2 on UI thread</item>
    /// </list>
    /// </para>
    /// </remarks>
    public void WriteData(string data)
    {
        if (_disposed || _webView == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        // Update output preview buffer for tooltip display
        UpdateOutputPreview(data);

        // RACE CONDITION PREVENTION: xterm.js may not be initialized when first SSH
        // data arrives. We buffer here to avoid losing the initial server banner/MOTD.
        if (!_isReady)
        {
            lock (_bufferLock)
            {
                _pendingData.Add(data);
                _logger.LogTrace("Buffered {Length} chars while waiting for terminal ready", data.Length);
            }
            return;
        }

        // BATCHING: Instead of sending each chunk immediately (expensive), accumulate
        // data and send in batches. The timer ensures we don't wait too long.
        lock (_writeBatchLock)
        {
            _writeBatch.Append(data);
        }

        // Only start timer if not already running (thread-safe check)
        if (System.Threading.Interlocked.CompareExchange(ref _timerRunning, 1, 0) == 0)
        {
            // We won the race - start a new timer
            _writeBatchTimer?.Dispose();
            _writeBatchTimer = new System.Threading.Timer(
                FlushWriteBatch,
                null,
                WriteBatchDelayMs,
                System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>
    /// Updates the output preview buffer with new terminal data.
    /// Strips ANSI escape sequences for clean text preview.
    /// </summary>
    private void UpdateOutputPreview(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        // Strip ANSI escape sequences for cleaner preview
        var cleanData = StripAnsiEscapeSequences(data);

        lock (_previewBufferLock)
        {
            _outputPreviewBuffer.Append(cleanData);

            // Keep only last MaxPreviewLength characters
            if (_outputPreviewBuffer.Length > MaxPreviewLength)
            {
                var excess = _outputPreviewBuffer.Length - MaxPreviewLength;
                _outputPreviewBuffer.Remove(0, excess);
            }

            // Notify subscribers of the update
            DataWritten?.Invoke(_outputPreviewBuffer.ToString());
        }
    }

    /// <summary>
    /// Gets the current output preview text (last ~200 characters).
    /// </summary>
    public string GetOutputPreview()
    {
        lock (_previewBufferLock)
        {
            return _outputPreviewBuffer.ToString();
        }
    }

    /// <summary>
    /// Strips ANSI escape sequences from terminal output for clean text preview.
    /// </summary>
    private static string StripAnsiEscapeSequences(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Simple ANSI escape sequence removal (ESC[...m for colors, ESC[...H for cursor, etc.)
        // This regex matches: ESC [ followed by any characters until a letter
        return System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[^@-~]*[@-~]", string.Empty);
    }

    private void FlushWriteBatch(object? state)
    {
        if (_disposed || _webView == null)
        {
            System.Threading.Interlocked.Exchange(ref _timerRunning, 0);
            return;
        }

        string? batch = null;
        lock (_writeBatchLock)
        {
            if (_writeBatch.Length > 0)
            {
                batch = _writeBatch.ToString();
                _writeBatch.Clear();
            }
        }

        // Reset the running flag before dispatching
        System.Threading.Interlocked.Exchange(ref _timerRunning, 0);

        // Send the batch if we have data
        if (!string.IsNullOrEmpty(batch))
        {
            // Must dispatch to UI thread for WebView2
            _webView?.Dispatcher.InvokeAsync(() =>
            {
                if (!_disposed)
                {
                    SendWriteMessage(batch);
                }
            }, System.Windows.Threading.DispatcherPriority.Send);
        }
    }

    private void SendWriteMessage(string data)
    {
        if (_disposed || _webView == null)
        {
            return;
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "write",
                Data = data
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write data to terminal");
        }
    }

    private void FlushPendingData()
    {
        List<string> dataToFlush;
        lock (_bufferLock)
        {
            if (_pendingData.Count == 0)
            {
                return;
            }

            dataToFlush = new List<string>(_pendingData);
            _pendingData.Clear();
        }

        _logger.LogDebug("Flushing {Count} pending data chunks to terminal", dataToFlush.Count);

        foreach (var data in dataToFlush)
        {
            SendWriteMessage(data);
        }
    }

    /// <summary>
    /// Resizes the terminal to the specified dimensions.
    /// </summary>
    /// <param name="cols">Number of columns.</param>
    /// <param name="rows">Number of rows.</param>
    public void Resize(int cols, int rows)
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        if (cols <= 0 || rows <= 0)
        {
            _logger.LogWarning("Invalid terminal dimensions: {Cols}x{Rows}", cols, rows);
            return;
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "resize",
                Cols = cols,
                Rows = rows
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);

            _logger.LogDebug("Sent resize command: {Cols}x{Rows}", cols, rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resize terminal");
        }
    }

    /// <summary>
    /// Sets the terminal theme colors.
    /// </summary>
    /// <param name="theme">An object containing xterm.js theme properties.</param>
    public void SetTheme(object theme)
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        if (theme == null)
        {
            throw new ArgumentNullException(nameof(theme));
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "setTheme",
                Theme = theme
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);

            _logger.LogDebug("Sent theme update");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set terminal theme");
        }
    }

    /// <summary>
    /// Sets the terminal font options.
    /// </summary>
    public void SetFont(string? fontFamily, double fontSize)
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(fontFamily) && fontSize <= 0)
        {
            return;
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "setFont",
                FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily,
                FontSize = fontSize > 0 ? fontSize : null
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);

            _logger.LogDebug("Sent font update");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set terminal font");
        }
    }

    /// <summary>
    /// Requests a terminal fit based on the current host size.
    /// </summary>
    public void Fit()
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "fit"
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request terminal fit");
        }
    }

    /// <summary>
    /// Focuses the terminal for keyboard input.
    /// </summary>
    public void Focus()
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "focus"
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to focus terminal");
        }
    }

    /// <summary>
    /// Clears the terminal display.
    /// </summary>
    public void Clear()
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        try
        {
            var message = new TerminalMessage
            {
                Type = "clear"
            };

            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);

            _logger.LogDebug("Sent clear command");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear terminal");
        }
    }

    /// <summary>
    /// Increases the terminal font size by one step.
    /// </summary>
    /// <returns>True if zoom was applied, false if already at max.</returns>
    public bool ZoomIn()
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return false;
        }

        var newSize = Math.Min(_fontSize + FontSizeStep, MaxFontSize);
        if (Math.Abs(newSize - _fontSize) < 0.01)
        {
            return false;
        }

        _fontSize = newSize;
        SetFont(null, _fontSize);
        _logger.LogDebug("Zoomed in to font size {FontSize}", _fontSize);
        return true;
    }

    /// <summary>
    /// Decreases the terminal font size by one step.
    /// </summary>
    /// <returns>True if zoom was applied, false if already at min.</returns>
    public bool ZoomOut()
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return false;
        }

        var newSize = Math.Max(_fontSize - FontSizeStep, MinFontSize);
        if (Math.Abs(newSize - _fontSize) < 0.01)
        {
            return false;
        }

        _fontSize = newSize;
        SetFont(null, _fontSize);
        _logger.LogDebug("Zoomed out to font size {FontSize}", _fontSize);
        return true;
    }

    /// <summary>
    /// Resets the terminal font size to the default.
    /// </summary>
    public void ResetZoom()
    {
        if (_disposed || _webView == null || !_isReady)
        {
            return;
        }

        _fontSize = DefaultFontSize;
        SetFont(null, _fontSize);
        _logger.LogDebug("Reset zoom to default font size {FontSize}", _fontSize);
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var json = e.WebMessageAsJson;
            var message = JsonSerializer.Deserialize<TerminalMessage>(json);

            if (message == null)
            {
                _logger.LogWarning("Received null message from terminal");
                return;
            }

            _logger.LogTrace("Received message from terminal: {Type}", message.Type);

            switch (message.Type)
            {
                case "input":
                    if (!string.IsNullOrEmpty(message.Data))
                    {
                        InputReceived?.Invoke(message.Data);
                    }
                    break;

                case "ready":
                    _isReady = true;
                    _logger.LogInformation("Terminal ready");
                    FlushPendingData();
                    TerminalReady?.Invoke();
                    break;

                case "resized":
                    if (message.Cols.HasValue && message.Rows.HasValue)
                    {
                        _columns = message.Cols.Value;
                        _rows = message.Rows.Value;
                        _logger.LogDebug("Terminal resized to {Cols}x{Rows}", _columns, _rows);
                        TerminalResized?.Invoke(_columns, _rows);
                    }
                    break;

                default:
                    _logger.LogWarning("Unknown message type from terminal: {Type}", message.Type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize terminal message");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling terminal message");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogDebug("Disposing WebTerminalBridge");

        // Clean up write batch timer
        lock (_writeBatchLock)
        {
            _writeBatchTimer?.Dispose();
            _writeBatchTimer = null;
        }

        if (_webView != null)
        {
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _webView = null;
        }

        _isReady = false;
    }

    /// <summary>
    /// Message structure for communication with the JavaScript terminal.
    /// </summary>
    private class TerminalMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Data { get; set; }

        [JsonPropertyName("cols")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Cols { get; set; }

        [JsonPropertyName("rows")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Rows { get; set; }

        [JsonPropertyName("theme")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Theme { get; set; }

        [JsonPropertyName("fontFamily")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FontFamily { get; set; }

        [JsonPropertyName("fontSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? FontSize { get; set; }
    }
}
