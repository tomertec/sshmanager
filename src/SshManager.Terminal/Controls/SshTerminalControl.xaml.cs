using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Controls;

/// <summary>
/// WPF terminal control for SSH sessions using WebTerminalControl (xterm.js + WebView2).
/// Uses xterm.js rendering for proper VT100/ANSI escape sequence support.
/// This includes full support for alternate screen buffer (mode 1049) used by docker, vim, etc.
/// </summary>
public partial class SshTerminalControl : UserControl
{
    private TerminalSession? _session;
    private SshTerminalBridge? _bridge;
    private bool _ownsBridge; // True if this control created the bridge, false if sharing
    private ILogger<SshTerminalControl> _logger = NullLogger<SshTerminalControl>.Instance;

    // Services
    private IBroadcastInputService? _broadcastService;
    private IServerStatsService? _serverStatsService;

    // Output buffer for search functionality
    private readonly TerminalOutputBuffer _outputBuffer;
    private TerminalTextSearchService? _searchService;

    // Decoder for UTF-8 conversion
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly object _decoderLock = new();

    // Stats tracking
    private readonly DispatcherTimer _statsTimer;
    private DateTimeOffset _lastStatsTime = DateTimeOffset.UtcNow;

    // Settings
    private string _fontFamily = "Cascadia Mono";
    private double _fontSize = 14;
    private bool _isPrimaryPane = true;
    private TerminalTheme? _currentTheme;

    // Resize tracking
    private int _lastColumns = 80;
    private int _lastRows = 24;

    // Disconnect tracking to prevent duplicate events
    private bool _disconnectedRaised;

    private static readonly string[] FontFallbacks =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Source Code Pro",
        "Source Code Pro Powerline",
        "Fira Code",
        "JetBrains Mono",
        "Courier New",
        "monospace"
    ];

    public SshTerminalControl()
    {
        InitializeComponent();

        // Initialize output buffer for search
        _outputBuffer = new TerminalOutputBuffer(10000);

        // Try to get logger from DI if available
        try
        {
            var loggerFactory = Application.Current?.TryFindResource("ILoggerFactory") as ILoggerFactory;
            if (loggerFactory != null)
            {
                _logger = loggerFactory.CreateLogger<SshTerminalControl>();
            }
        }
        catch
        {
            // Logger not available, use null logger
        }

        // Set up stats update timer (1 second interval)
        _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statsTimer.Tick += StatsTimer_Tick;

        // Wire up find overlay events
        FindOverlay.CloseRequested += FindOverlay_CloseRequested;
        FindOverlay.NavigateToLine += FindOverlay_NavigateToLine;
        FindOverlay.SearchResultsChanged += FindOverlay_SearchResultsChanged;

        // Wire up terminal events
        TerminalHost.TerminalReady += OnTerminalReady;
        TerminalHost.InputReceived += OnTerminalInputReceived;
        TerminalHost.TerminalResized += OnTerminalResized;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize search service with output buffer
        _searchService ??= new TerminalTextSearchService(_outputBuffer);
        FindOverlay.SetSearchService(_searchService);

        // Restart stats timer if we have an active session
        if (_session?.IsConnected == true && !_statsTimer.IsEnabled)
        {
            _statsTimer.Start();
        }

        _logger.LogDebug("SshTerminalControl loaded");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Pause stats updates while not visible (saves resources)
        _statsTimer.Stop();
        _logger.LogDebug("SshTerminalControl unloaded (pausing stats)");
    }

    private void OnTerminalReady()
    {
        _logger.LogDebug("WebTerminalControl ready");

        // Apply theme if available
        if (_currentTheme != null)
        {
            ApplyTheme(_currentTheme);
        }

        ApplyFontSettings();

        // Auto-focus the terminal when it becomes ready
        TerminalHost.Focus();
    }

    private void OnTerminalInputReceived(string input)
    {
        if (string.IsNullOrEmpty(input)) return;

        // If broadcast mode is enabled, send to all selected sessions
        if (_broadcastService?.IsEnabled == true)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            _broadcastService.SendToSelected(bytes);
        }
        else
        {
            _bridge?.SendText(input);
        }
    }

    private void OnTerminalResized(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0) return;
        if (cols == _lastColumns && rows == _lastRows) return;

        _lastColumns = cols;
        _lastRows = rows;

        // Resize SSH terminal
        if (_session?.Connection != null)
        {
            bool success = _session.Connection.ResizeTerminal((uint)cols, (uint)rows);
            if (success)
            {
                _logger.LogDebug("Terminal resized to {Cols}x{Rows}", cols, rows);
            }
            else
            {
                _logger.LogWarning("Terminal resize to {Cols}x{Rows} failed", cols, rows);
            }
        }
    }

    /// <summary>
    /// Handles data received from SSH and displays it in the terminal.
    /// </summary>
    private void OnSshDataReceived(byte[] data)
    {
        if (data.Length == 0) return;

        // Decode using a stateful UTF-8 decoder to avoid splitting multibyte sequences
        var text = DecodeUtf8(data);

        if (string.IsNullOrEmpty(text)) return;

        // Capture to buffer for search functionality
        // Buffer automatically trims old data when it exceeds MaxLines to prevent unbounded growth
        _outputBuffer.AppendOutput(text);

        // Write to WebTerminal - the bridge handles batching and UI thread dispatch
        TerminalHost.WriteData(text);
    }

    #region Keyboard Handling

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Handle Ctrl+F for Find
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowFindOverlay();
            e.Handled = true;
            return;
        }

        // Handle Escape to close find overlay
        if (e.Key == Key.Escape && FindOverlay.Visibility == Visibility.Visible)
        {
            HideFindOverlay();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+C for Copy
        if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CopyToClipboard();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+V for Paste
        if (e.Key == Key.V && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }
    }

    #endregion

    #region Find Overlay

    /// <summary>
    /// Shows the find overlay for searching terminal output.
    /// </summary>
    public void ShowFindOverlay()
    {
        FindOverlay.Show();
    }

    /// <summary>
    /// Hides the find overlay.
    /// </summary>
    public void HideFindOverlay()
    {
        FindOverlay.Hide();
        TerminalHost.Focus();
    }

    private void FindOverlay_CloseRequested(object? sender, EventArgs e)
    {
        HideFindOverlay();
    }

    private void FindOverlay_NavigateToLine(object? sender, int lineIndex)
    {
        // Note: xterm.js manages its own scrollback.
        // The search match is indicated in the overlay.
        _logger.LogDebug("Navigate to line {LineIndex} requested", lineIndex);
    }

    private void FindOverlay_SearchResultsChanged(object? sender, EventArgs e)
    {
        // Search results changed - terminal automatically highlights matches
    }

    #endregion

    #region UTF-8 Decoding

    private string DecodeUtf8(byte[] data)
    {
        lock (_decoderLock)
        {
            var charBuffer = ArrayPool<char>.Shared.Rent(data.Length + 4);
            try
            {
                var charCount = _decoder.GetChars(data, 0, data.Length, charBuffer, 0, flush: false);
                return charCount > 0 ? new string(charBuffer, 0, charCount) : string.Empty;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }
    }

    #endregion

    #region Clipboard Operations

    /// <summary>
    /// Copies selected text to the clipboard.
    /// </summary>
    public void CopyToClipboard()
    {
        try
        {
            // WebTerminalControl handles selection internally via xterm.js
            _logger.LogDebug("Copy to clipboard requested");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy to clipboard");
        }
    }

    /// <summary>
    /// Pastes text from the clipboard to the terminal.
    /// </summary>
    public void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    // Send pasted text to SSH
                    _bridge?.SendText(text);
                    _logger.LogDebug("Pasted {CharCount} characters from clipboard", text.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to paste from clipboard");
        }
    }

    #endregion

    #region Connection Management

    /// <summary>
    /// Connects to an SSH server using the provided connection info.
    /// </summary>
    public Task ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback = null,
        CancellationToken cancellationToken = default)
    {
        return ConnectAsync(sshService, connectionInfo, hostKeyCallback, null, cancellationToken);
    }

    /// <summary>
    /// Connects to an SSH server using direct SSH.NET connection.
    /// </summary>
    public async Task ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken cancellationToken = default)
    {
        if (connectionInfo == null)
        {
            throw new ArgumentNullException(nameof(connectionInfo));
        }

        // Reset disconnect tracking for new connection
        _disconnectedRaised = false;

        var newSession = DataContext as TerminalSession;
        _session = newSession;

        try
        {
            ShowStatus("Connecting...");

            // Initialize WebTerminalControl
            await TerminalHost.InitializeAsync();

            // Establish SSH connection
            _logger.LogInformation("Connecting to {Host}:{Port}", connectionInfo.Hostname, connectionInfo.Port);
            var sshConnection = await sshService.ConnectAsync(
                connectionInfo,
                hostKeyCallback,
                kbInteractiveCallback,
                (uint)_lastColumns,
                (uint)_lastRows,
                cancellationToken);

            // Update session with connection
            if (_session != null)
            {
                _session.Connection = sshConnection;
            }

            // Create SSH bridge and store in session for sharing with mirror panes
            _bridge = new SshTerminalBridge(sshConnection.ShellStream);
            _bridge.DataReceived += OnSshDataReceived;
            _bridge.Disconnected += OnBridgeDisconnected;
            _ownsBridge = true; // This control owns the bridge

            // Store bridge in session for mirrored panes to reuse
            if (_session != null)
            {
                _session.Bridge = _bridge;
            }

            // Start reading SSH data
            _bridge.StartReading();

            HideStatus();
            _statsTimer.Start();
            if (_session != null)
            {
                StatusBar.Stats = _session.Stats;
                StatusBar.Visibility = Visibility.Visible;
            }

            _logger.LogInformation("Connected to {Host}", connectionInfo.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to {Host}", connectionInfo.Hostname);
            ShowStatus($"Connection failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Connects to an SSH server through a proxy chain.
    /// </summary>
    public async Task ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken cancellationToken = default)
    {
        if (connectionChain == null || connectionChain.Count == 0)
        {
            throw new ArgumentException("Connection chain cannot be empty", nameof(connectionChain));
        }

        // Reset disconnect tracking for new connection
        _disconnectedRaised = false;

        var newSession = DataContext as TerminalSession;
        _session = newSession;

        try
        {
            ShowStatus("Connecting through proxy chain...");

            // Initialize WebTerminalControl
            await TerminalHost.InitializeAsync();

            // Establish SSH connection through proxy chain
            var targetHost = connectionChain[^1];
            _logger.LogInformation("Connecting to {Host} through proxy chain", targetHost.Hostname);

            var sshConnection = await sshService.ConnectWithProxyChainAsync(
                connectionChain,
                hostKeyCallback,
                kbInteractiveCallback,
                (uint)_lastColumns,
                (uint)_lastRows,
                cancellationToken);

            // Update session with connection
            if (_session != null)
            {
                _session.Connection = sshConnection;
            }

            // Create SSH bridge and store in session for sharing with mirror panes
            _bridge = new SshTerminalBridge(sshConnection.ShellStream);
            _bridge.DataReceived += OnSshDataReceived;
            _bridge.Disconnected += OnBridgeDisconnected;
            _ownsBridge = true; // This control owns the bridge

            // Store bridge in session for mirrored panes to reuse
            if (_session != null)
            {
                _session.Bridge = _bridge;
            }

            // Start reading SSH data
            _bridge.StartReading();

            HideStatus();
            _statsTimer.Start();
            if (_session != null)
            {
                StatusBar.Stats = _session.Stats;
                StatusBar.Visibility = Visibility.Visible;
            }

            var hosts = string.Join(" → ", connectionChain.Select(c => c.Hostname));
            _logger.LogInformation("Connected through proxy chain: {Chain}", hosts);
        }
        catch (Exception ex)
        {
            var chainHosts = string.Join(" → ", connectionChain.Select(c => c.Hostname));
            _logger.LogError(ex, "Failed to connect through proxy chain: {Chain}", chainHosts);
            ShowStatus($"Connection failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Attaches to an existing session that is already connected.
    /// Used when mirroring a session into a split pane.
    /// </summary>
    public async Task AttachToSessionAsync(TerminalSession session)
    {
        _session = session;

        if (_session.IsConnected)
        {
            // Initialize WebTerminalControl first
            await TerminalHost.InitializeAsync();

            // Reuse existing bridge from session if available (for mirroring)
            // This prevents multiple readers from competing on the same ShellStream
            if (_session.Bridge != null)
            {
                _bridge = _session.Bridge;
                _bridge.DataReceived += OnSshDataReceived;
                _ownsBridge = false; // This control is sharing the bridge
                // Don't call StartReading - the bridge is already reading
                _logger.LogDebug("Attached to existing bridge for mirrored session: {Title}", session.Title);
            }
            else if (_session.Connection?.ShellStream != null)
            {
                // Fallback: create new bridge if session doesn't have one
                _bridge = new SshTerminalBridge(_session.Connection.ShellStream);
                _bridge.DataReceived += OnSshDataReceived;
                _bridge.Disconnected += OnBridgeDisconnected;
                _session.Bridge = _bridge;
                _ownsBridge = true; // This control owns the bridge
                _bridge.StartReading();
                _logger.LogDebug("Created new bridge for session: {Title}", session.Title);
            }

            HideStatus();
            _statsTimer.Start();
            StatusBar.Stats = _session.Stats;
            StatusBar.Visibility = Visibility.Visible;
        }
        else
        {
            _statsTimer.Stop();
            ShowStatus("Disconnected");
        }
    }

    /// <summary>
    /// Attaches to an existing session that is already connected (synchronous wrapper).
    /// </summary>
    [Obsolete("Use AttachToSessionAsync instead for proper WebView2 initialization")]
    public void AttachToSession(TerminalSession session)
    {
        _ = AttachToSessionAsync(session);
    }

    /// <summary>
    /// Disconnects from the SSH server.
    /// </summary>
    public void Disconnect()
    {
        _statsTimer.Stop();

        if (_bridge != null)
        {
            _bridge.DataReceived -= OnSshDataReceived;
            _bridge.Disconnected -= OnBridgeDisconnected;

            // Only dispose the bridge if this control owns it
            // Shared bridges are disposed when the session closes
            if (_ownsBridge)
            {
                _bridge.Dispose();
            }
            _bridge = null;
        }

        if (_session?.Connection != null)
        {
            _session.Connection.Disconnected -= OnConnectionDisconnected;
            _session.Connection.Dispose();
        }

        ShowStatus("Disconnected");
    }

    private void OnConnectionDisconnected(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _statsTimer.Stop();
            ShowStatus("Disconnected");
        });
    }

    private void OnBridgeDisconnected(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _statsTimer.Stop();
            ShowStatus("Disconnected");
            _logger.LogInformation("SSH connection disconnected for session: {Title}", _session?.Title);

            // Raise the Disconnected event to notify parent controls (only once)
            if (!_disconnectedRaised)
            {
                _disconnectedRaised = true;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    #endregion

    #region Stats Timer

    private async void StatsTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_session == null || _bridge == null) return;

            var now = DateTimeOffset.UtcNow;

            // Update uptime
            _session.Stats.Uptime = now - _session.CreatedAt;

            // Update throughput from bridge
            _session.Stats.BytesSent = _bridge.TotalBytesSent;
            _session.Stats.BytesReceived = _bridge.TotalBytesReceived;

            // Calculate throughput per second
            var elapsed = (now - _lastStatsTime).TotalSeconds;
            if (elapsed > 0)
            {
                _session.Stats.BytesSentPerSecond = (_bridge.TotalBytesSent - _session.TotalBytesSent) / elapsed;
                _session.Stats.BytesReceivedPerSecond = (_bridge.TotalBytesReceived - _session.TotalBytesReceived) / elapsed;
            }

            _session.TotalBytesSent = _bridge.TotalBytesSent;
            _session.TotalBytesReceived = _bridge.TotalBytesReceived;
            _lastStatsTime = now;

            // Collect server stats via SSH (only every ~10 seconds)
            if (_session.Connection?.IsConnected == true && _serverStatsService != null && now.Second % 10 == 0)
            {
                try
                {
                    var stats = await _serverStatsService.GetStatsAsync(_session.Connection);
                    _session.Stats.CpuUsage = stats.CpuUsage;
                    _session.Stats.MemoryUsage = stats.MemoryUsage;
                    _session.Stats.DiskUsage = stats.DiskUsage;
                    _session.Stats.ServerUptime = stats.ServerUptime;
                }
                catch
                {
                    // Ignore stats collection failures
                }
            }

            StatusBar.Stats = _session.Stats;
            StatusBar.UpdateDisplay();
        }
        catch (Exception ex)
        {
            // Catch all exceptions in async void event handler to prevent application crashes
            _logger.LogError(ex, "Error updating terminal stats");
        }
    }

    #endregion

    #region Status Overlay

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusProgress.Visibility = message.Contains("Connecting")
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        StatusOverlay.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Public Properties and Methods

    /// <summary>
    /// Gets or sets the terminal font family.
    /// </summary>
    public string TerminalFontFamily
    {
        get => _fontFamily;
        set
        {
            _fontFamily = value;
            ApplyFontSettings();
        }
    }

    /// <summary>
    /// Gets or sets the terminal font size.
    /// </summary>
    public double TerminalFontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            ApplyFontSettings();
        }
    }

    /// <summary>
    /// Gets whether the terminal is connected.
    /// </summary>
    public bool IsConnected => _session?.Connection?.IsConnected == true;

    /// <summary>
    /// Gets or sets the scrollback buffer size.
    /// </summary>
    public int ScrollbackBufferSize
    {
        get => _outputBuffer.MaxLines;
        set => _outputBuffer.MaxLines = value;
    }

    /// <summary>
    /// Event raised when the terminal title changes.
    /// </summary>
#pragma warning disable CS0067 // Event is never used - public API for future use
    public event EventHandler<string>? TitleChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Event raised when the SSH connection is disconnected (either by remote or error).
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Focuses the terminal input.
    /// </summary>
    public void FocusInput()
    {
        TerminalHost.Focus();
    }

    /// <summary>
    /// Sends a command string to the terminal, followed by a carriage return.
    /// </summary>
    public void SendCommand(string command)
    {
        _bridge?.SendCommand(command);
    }

    /// <summary>
    /// Gets or sets whether this control is the primary pane for its session.
    /// </summary>
    public bool IsPrimaryPane
    {
        get => _isPrimaryPane;
        set => _isPrimaryPane = value;
    }

    /// <summary>
    /// Sets the broadcast service for sending input to multiple sessions.
    /// </summary>
    public void SetBroadcastService(IBroadcastInputService? service)
    {
        _broadcastService = service;
    }

    /// <summary>
    /// Sets the server stats service for collecting CPU/memory/disk usage.
    /// </summary>
    public void SetServerStatsService(IServerStatsService? service)
    {
        _serverStatsService = service;
    }

    /// <summary>
    /// Applies a terminal color theme.
    /// </summary>
    public void ApplyTheme(TerminalTheme theme)
    {
        if (theme == null)
        {
            _logger.LogWarning("ApplyTheme called with null theme");
            return;
        }

        try
        {
            _currentTheme = theme;

            // Convert to xterm.js theme format
            var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

            // Apply to WebTerminalControl
            TerminalHost.SetTheme(xtermTheme);

            _logger.LogDebug("Applied theme: {ThemeName}", theme.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply theme: {ThemeName}", theme.Name);
        }
    }

    private void ApplyFontSettings()
    {
        var fontFamily = string.IsNullOrWhiteSpace(_fontFamily)
            ? "Cascadia Mono"
            : _fontFamily;
        var fontSize = _fontSize > 0 ? _fontSize : 14;

        var fontStack = BuildFontStack(fontFamily);
        TerminalHost.SetFont(fontStack, fontSize);
    }

    private static string BuildFontStack(string preferredFont)
    {
        var fonts = new List<string>(FontFallbacks.Length + 1)
        {
            QuoteIfNeeded(preferredFont)
        };

        foreach (var fallback in FontFallbacks)
        {
            if (fonts.Any(font => font.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            fonts.Add(QuoteIfNeeded(fallback));
        }

        return string.Join(", ", fonts);
    }

    private static string QuoteIfNeeded(string font)
    {
        var trimmed = font.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
            (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            return trimmed;
        }

        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Contains(','))
        {
            return $"\"{trimmed.Replace("\"", "\\\"")}\"";
        }

        return trimmed;
    }

    /// <summary>
    /// Gets the currently applied theme.
    /// </summary>
    public TerminalTheme? CurrentTheme => _currentTheme;

    /// <summary>
    /// Gets or sets the broadcast input service.
    /// </summary>
    public IBroadcastInputService? BroadcastService
    {
        get => _broadcastService;
        set => _broadcastService = value;
    }

    /// <summary>
    /// Gets the terminal output buffer for search/export.
    /// </summary>
    public TerminalOutputBuffer OutputBuffer => _outputBuffer;

    /// <summary>
    /// Gets the text content of the terminal output buffer.
    /// Useful for exporting session logs.
    /// </summary>
    public string GetOutputText()
    {
        return _outputBuffer.GetAllText();
    }

    /// <summary>
    /// Clears the output buffer.
    /// </summary>
    public void ClearOutputBuffer()
    {
        _outputBuffer.Clear();
    }

    #endregion
}
