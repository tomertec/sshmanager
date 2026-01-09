using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Controls;

/// <summary>
/// WPF UserControl that hosts a WebView2-based terminal using xterm.js.
/// Provides a modern web-based terminal interface with full VT100/ANSI support.
/// </summary>
public partial class WebTerminalControl : UserControl, IDisposable
{
    private readonly ILogger<WebTerminalControl> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private WebTerminalBridge? _bridge;
    private bool _disposed;
    private bool _isInitialized;
    private const int FitDebounceMs = 100;
    private readonly DispatcherTimer _fitDebounceTimer;
    private TaskCompletionSource<bool>? _readyTcs;
    private bool _hasFocus;

    /// <summary>
    /// Gets the bridge that manages communication between C# and the JavaScript terminal.
    /// </summary>
    public WebTerminalBridge? Bridge => _bridge;

    /// <summary>
    /// Gets whether this terminal control currently has keyboard focus.
    /// </summary>
    public bool HasTerminalFocus => _hasFocus;

    /// <summary>
    /// Gets whether the terminal is ready to receive commands.
    /// </summary>
    public bool IsTerminalReady => _bridge?.IsReady ?? false;

    /// <summary>
    /// Gets the current number of columns in the terminal.
    /// </summary>
    public int Columns => _bridge?.Columns ?? 80;

    /// <summary>
    /// Gets the current number of rows in the terminal.
    /// </summary>
    public int Rows => _bridge?.Rows ?? 24;

    /// <summary>
    /// Event raised when user types in the terminal.
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
    /// Event raised when the terminal gains or loses keyboard focus.
    /// Parameter is true when focused, false when unfocused.
    /// </summary>
    public event Action<bool>? FocusChanged;

    /// <summary>
    /// Event raised when data is written to the terminal.
    /// Used for capturing output preview for tab tooltips.
    /// The string parameter contains the last ~200 characters of output.
    /// </summary>
    public event Action<string>? DataWritten;

    /// <summary>
    /// Default constructor for XAML.
    /// </summary>
    public WebTerminalControl() : this(null, null)
    {
    }

    /// <summary>
    /// Constructor with optional logger.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public WebTerminalControl(ILogger<WebTerminalControl>? logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Constructor with optional logger and logger factory.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="loggerFactory">Optional logger factory for creating child component loggers.</param>
    public WebTerminalControl(ILogger<WebTerminalControl>? logger, ILoggerFactory? loggerFactory)
    {
        _logger = logger ?? NullLogger<WebTerminalControl>.Instance;
        _loggerFactory = loggerFactory;
        InitializeComponent();
        _fitDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(FitDebounceMs)
        };
        _fitDebounceTimer.Tick += FitDebounceTimer_Tick;
    }

    /// <summary>
    /// Initializes the WebView2 control and loads the terminal HTML.
    /// Waits for the terminal to be ready before returning.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebTerminalControl));
        }

        if (_isInitialized)
        {
            _logger.LogWarning("InitializeAsync called but control already initialized");
            return;
        }

        try
        {
            _logger.LogDebug("Initializing WebTerminalControl");

            // Create TaskCompletionSource to wait for terminal ready
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Ensure WebView2 runtime is initialized
            await WebViewControl.EnsureCoreWebView2Async();

            // Disable browser accelerator keys (Ctrl+W, Ctrl+N, Ctrl+T, etc.)
            // This allows these keys to pass through to xterm.js instead of being
            // handled by WebView2 as browser shortcuts
            WebViewControl.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // Track focus state via WebView2's GotFocus/LostFocus events
            // This is more reliable than WPF's Keyboard.FocusedElement for WebView2
            WebViewControl.GotFocus += OnWebViewGotFocus;
            WebViewControl.LostFocus += OnWebViewLostFocus;

            // Create and initialize the bridge with correctly typed logger
            var bridgeLogger = _loggerFactory?.CreateLogger<WebTerminalBridge>()
                ?? NullLogger<WebTerminalBridge>.Instance;
            _bridge = new WebTerminalBridge(bridgeLogger);
            await _bridge.InitializeAsync(WebViewControl);

            // Forward bridge events
            _bridge.InputReceived += OnBridgeInputReceived;
            _bridge.TerminalReady += OnBridgeTerminalReady;
            _bridge.TerminalResized += OnBridgeTerminalResized;
            _bridge.DataWritten += OnBridgeDataWritten;

            // Load terminal HTML
            await LoadTerminalHtmlAsync();

            // Wait for terminal to be ready (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _readyTcs.Task.WaitAsync(cts.Token);
                _logger.LogDebug("Terminal ready signal received");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout waiting for terminal ready, proceeding anyway");
            }

            _isInitialized = true;
            _logger.LogInformation("WebTerminalControl initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebTerminalControl");
            throw;
        }
    }

    /// <summary>
    /// Sends data to the terminal for display.
    /// </summary>
    /// <param name="data">The text data to write to the terminal.</param>
    public void WriteData(string data)
    {
        if (_disposed)
        {
            _logger.LogWarning("WriteData called on disposed control");
            return;
        }

        _bridge?.WriteData(data);
    }

    /// <summary>
    /// Sets the terminal theme colors.
    /// </summary>
    /// <param name="theme">Dictionary containing xterm.js theme properties.</param>
    public void SetTheme(Dictionary<string, string> theme)
    {
        if (_disposed)
        {
            _logger.LogWarning("SetTheme called on disposed control");
            return;
        }

        if (theme == null)
        {
            throw new ArgumentNullException(nameof(theme));
        }

        _bridge?.SetTheme(theme);
    }

    /// <summary>
    /// Sets the terminal font family and size.
    /// </summary>
    public void SetFont(string? fontFamily, double fontSize)
    {
        if (_disposed)
        {
            _logger.LogWarning("SetFont called on disposed control");
            return;
        }

        _bridge?.SetFont(fontFamily, fontSize);
    }

    /// <summary>
    /// Requests a terminal fit based on the current host size.
    /// </summary>
    public void RequestFit()
    {
        if (_disposed)
        {
            return;
        }

        _bridge?.Fit();
    }

    /// <summary>
    /// Forces a visual refresh of the WebView2 control.
    /// Call this when the control becomes visible after being hidden,
    /// as WebView2 may not properly repaint after visibility transitions.
    /// </summary>
    public void RefreshVisual()
    {
        if (_disposed)
        {
            return;
        }

        // Invalidate the visual to force a repaint
        WebViewControl?.InvalidateVisual();

        // Also request a fit to ensure proper dimensions
        _bridge?.Fit();
    }

    /// <summary>
    /// Focuses the terminal for keyboard input.
    /// </summary>
    public new void Focus()
    {
        if (_disposed)
        {
            return;
        }

        if (WebViewControl != null)
        {
            // Ensure keyboard focus lands on the WebView2 host.
            WebViewControl.Focus();
            Keyboard.Focus(WebViewControl);
        }

        _bridge?.Focus();
    }

    /// <summary>
    /// Clears the terminal display.
    /// </summary>
    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        _bridge?.Clear();
    }

    /// <summary>
    /// Increases the terminal font size by one step.
    /// </summary>
    /// <returns>True if zoom was applied, false if already at max.</returns>
    public bool ZoomIn()
    {
        if (_disposed)
        {
            return false;
        }

        return _bridge?.ZoomIn() ?? false;
    }

    /// <summary>
    /// Decreases the terminal font size by one step.
    /// </summary>
    /// <returns>True if zoom was applied, false if already at min.</returns>
    public bool ZoomOut()
    {
        if (_disposed)
        {
            return false;
        }

        return _bridge?.ZoomOut() ?? false;
    }

    /// <summary>
    /// Resets the terminal font size to the default.
    /// </summary>
    public void ResetZoom()
    {
        if (_disposed)
        {
            return;
        }

        _bridge?.ResetZoom();
    }

    /// <summary>
    /// Gets the current font size.
    /// </summary>
    public double CurrentFontSize => _bridge?.FontSize ?? WebTerminalBridge.DefaultFontSize;

    private async Task LoadTerminalHtmlAsync()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Debug: List all embedded resources to verify the correct name
            var resourceNames = assembly.GetManifestResourceNames();
            _logger.LogDebug("Available embedded resources: {Resources}", string.Join(", ", resourceNames));

            // Find the terminal.html resource (case-insensitive search)
            var actualResourceName = FindResourceName(resourceNames, "terminal.html");

            if (actualResourceName == null)
            {
                throw new FileNotFoundException(
                    $"Embedded resource 'terminal.html' not found. Available resources: {string.Join(", ", resourceNames)}");
            }

            _logger.LogDebug("Loading terminal HTML from resource: {ResourceName}", actualResourceName);

            using var stream = assembly.GetManifestResourceStream(actualResourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Failed to load embedded resource: {actualResourceName}");
            }

            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            html = InjectPowerlineFonts(html, assembly, resourceNames);

            // Navigate to the HTML content
            WebViewControl.NavigateToString(html);
            _logger.LogDebug("Terminal HTML loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load terminal HTML");
            throw;
        }
    }

    private static string? FindResourceName(IEnumerable<string> resourceNames, string suffix)
    {
        return resourceNames.FirstOrDefault(r => r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string LoadEmbeddedResourceAsBase64(
        Assembly assembly,
        IEnumerable<string> resourceNames,
        string suffix)
    {
        var resourceName = FindResourceName(resourceNames, suffix);
        if (resourceName == null)
        {
            throw new FileNotFoundException(
                $"Embedded resource '{suffix}' not found. Available resources: {string.Join(", ", resourceNames)}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Failed to load embedded resource: {resourceName}");
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private static string InjectPowerlineFonts(string html, Assembly assembly, IEnumerable<string> resourceNames)
    {
        var regular = LoadEmbeddedResourceAsBase64(
            assembly,
            resourceNames,
            "SourceCodePro-Powerline-Regular.otf");
        var bold = LoadEmbeddedResourceAsBase64(
            assembly,
            resourceNames,
            "SourceCodePro-Powerline-Bold.otf");
        var italic = LoadEmbeddedResourceAsBase64(
            assembly,
            resourceNames,
            "SourceCodePro-Powerline-Italic.otf");
        var boldItalic = LoadEmbeddedResourceAsBase64(
            assembly,
            resourceNames,
            "SourceCodePro-Powerline-BoldItalic.otf");

        return html
            .Replace("{{POWERLINE_SCP_REGULAR}}", regular)
            .Replace("{{POWERLINE_SCP_BOLD}}", bold)
            .Replace("{{POWERLINE_SCP_ITALIC}}", italic)
            .Replace("{{POWERLINE_SCP_BOLDITALIC}}", boldItalic);
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("WebTerminalControl loaded");
        ScheduleFit();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        // Note: Do NOT dispose here. WPF fires Unloaded when switching tabs,
        // which would destroy the terminal state. Disposal should only happen
        // when the session is explicitly closed via Dispose().
        _logger.LogDebug("WebTerminalControl unloaded (keeping state)");
    }

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleFit();
    }

    private void UserControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Handle Ctrl+MouseWheel for zoom
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Delta > 0)
            {
                ZoomIn();
            }
            else if (e.Delta < 0)
            {
                ZoomOut();
            }

            e.Handled = true;
        }
    }

    private void ScheduleFit()
    {
        if (_disposed)
        {
            return;
        }

        _fitDebounceTimer.Stop();
        _fitDebounceTimer.Start();
    }

    private void FitDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _fitDebounceTimer.Stop();
        RequestFit();
    }

    private void OnBridgeInputReceived(string data)
    {
        InputReceived?.Invoke(data);
    }

    private void OnBridgeTerminalReady()
    {
        TerminalReady?.Invoke();
    }

    private void OnBridgeTerminalResized(int cols, int rows)
    {
        // Signal that initialization can complete (we have the actual size now)
        _readyTcs?.TrySetResult(true);
        TerminalResized?.Invoke(cols, rows);
    }

    private void OnBridgeDataWritten(string preview)
    {
        DataWritten?.Invoke(preview);
    }

    private void OnWebViewGotFocus(object sender, RoutedEventArgs e)
    {
        if (!_hasFocus)
        {
            _hasFocus = true;
            _logger.LogDebug("Terminal gained focus");
            FocusChanged?.Invoke(true);
        }
    }

    private void OnWebViewLostFocus(object sender, RoutedEventArgs e)
    {
        if (_hasFocus)
        {
            _hasFocus = false;
            _logger.LogDebug("Terminal lost focus");
            FocusChanged?.Invoke(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogDebug("Disposing WebTerminalControl");
        _fitDebounceTimer.Stop();
        _fitDebounceTimer.Tick -= FitDebounceTimer_Tick;

        if (_bridge != null)
        {
            _bridge.InputReceived -= OnBridgeInputReceived;
            _bridge.TerminalReady -= OnBridgeTerminalReady;
            _bridge.TerminalResized -= OnBridgeTerminalResized;
            _bridge.Dispose();
            _bridge = null;
        }

        if (WebViewControl != null)
        {
            try
            {
                // Unhook focus tracking events
                WebViewControl.GotFocus -= OnWebViewGotFocus;
                WebViewControl.LostFocus -= OnWebViewLostFocus;

                WebViewControl.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing WebView2");
            }
        }

        // Clear focus state
        if (_hasFocus)
        {
            _hasFocus = false;
            FocusChanged?.Invoke(false);
        }
    }
}
