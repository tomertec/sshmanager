using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Controls;

/// <summary>
/// WPF terminal control for SSH sessions using WebTerminalControl (xterm.js + WebView2).
/// Uses xterm.js rendering for proper VT100/ANSI escape sequence support.
/// This includes full support for alternate screen buffer (mode 1049) used by docker, vim, etc.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architecture Overview:</b> This control orchestrates the terminal session by connecting
/// multiple components:
/// </para>
/// <code>
/// ┌─────────────────────────────────────────────────────────────────┐
/// │                    SshTerminalControl                           │
/// │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
/// │  │TerminalHost │  │ StatusBar   │  │ FindOverlay             │  │
/// │  │(WebTerminal │  │             │  │ (Search UI)             │  │
/// │  │ Control)    │  │             │  │                         │  │
/// │  └──────┬──────┘  └─────────────┘  └─────────────────────────┘  │
/// │         │                                                       │
/// │  ┌──────┴──────┐  ┌─────────────┐  ┌─────────────────────────┐  │
/// │  │ WebTerminal │  │ SshTerminal │  │ TerminalOutput          │  │
/// │  │ Bridge      │  │ Bridge      │  │ Buffer                  │  │
/// │  │ (C# ↔ JS)   │  │ (SSH ↔ C#)  │  │ (Search/Export)         │  │
/// │  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
/// └─────────────────────────────────────────────────────────────────┘
/// </code>
/// <para>
/// <b>Session Ownership:</b> When created via ConnectAsync(), this control owns the bridge
/// and connection. When attached via AttachToSessionAsync() (for split panes), the control
/// shares the bridge with the original pane. The _ownsBridge flag tracks ownership for
/// proper cleanup.
/// </para>
/// <para>
/// <b>Thread Safety:</b> All public methods must be called on the UI thread. Internal
/// event handlers from bridges may fire on background threads and are marshaled via
/// Dispatcher.Invoke where needed.
/// </para>
/// </remarks>
public partial class SshTerminalControl : UserControl, IKeyboardHandlerContext, INotifyPropertyChanged
{
    private TerminalSession? _session;
    private SshTerminalBridge? _bridge;
    private SerialTerminalBridge? _serialBridge;

    // Serial control commands
    private ICommand? _toggleDtrCommand;
    private ICommand? _toggleRtsCommand;
    private ICommand? _sendBreakCommand;
    private ICommand? _toggleLocalEchoCommand;

    // OWNERSHIP TRACKING: Critical for cleanup. When we create a connection (ConnectAsync),
    // we own the bridge and must dispose it. When we attach to an existing session
    // (AttachToSessionAsync for split panes), we share the bridge and must NOT dispose it.
    private bool _ownsBridge;

    private ILogger<SshTerminalControl> _logger = NullLogger<SshTerminalControl>.Instance;

    // EXTRACTED SERVICES: These were refactored out of this control to reduce complexity.
    // Each handles a specific concern: keyboard input, clipboard operations, connection logic.
    private readonly ITerminalKeyboardHandler _keyboardHandler;
    private readonly ITerminalClipboardService _clipboardService;
    private ITerminalStatsCollector? _statsCollector;
    private readonly ITerminalConnectionHandler _connectionHandler;

    // Optional services injected at runtime
    private IBroadcastInputService? _broadcastService;
    private IServerStatsService? _serverStatsService;
    private ITerminalFocusTracker? _focusTracker;

    // Serial connection service stored for reconnection
    private ISerialConnectionService? _serialService;
    private SerialConnectionInfo? _lastSerialConnectionInfo;

    // Auto-reconnect configuration
    private bool _autoReconnectEnabled;
    private int _maxReconnectAttempts = 3;
    private int _reconnectAttemptCount;
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(2);
    private bool _isReconnecting;

    // OUTPUT BUFFER: Captures all terminal output for search functionality and session export.
    // Uses a tiered storage strategy: recent lines in memory, older lines compressed to disk.
    private readonly TerminalOutputBuffer _outputBuffer;
    private TerminalTextSearchService? _searchService;

    // UTF-8 DECODING: SSH data arrives as raw bytes. We need stateful decoding because
    // multi-byte UTF-8 sequences may be split across TCP packets. The Decoder maintains
    // state between calls to handle partial sequences correctly.
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly object _decoderLock = new();

    // Settings
    private string _fontFamily = "Cascadia Mono";
    private double _fontSize = 14;
    private bool _isPrimaryPane = true;
    private TerminalTheme? _currentTheme;

    // RESIZE TRACKING: We track the last sent dimensions to avoid spamming the server
    // with redundant resize requests when the user drags the window edge.
    private int _lastColumns = 80;
    private int _lastRows = 24;

    // DISCONNECT TRACKING: Prevents firing the Disconnected event multiple times when
    // both the bridge and connection signal disconnect (e.g., server closes connection).
    private bool _disconnectedRaised;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public SshTerminalControl()
        : this(null, null, null)
    {
    }

    public SshTerminalControl(
        ITerminalKeyboardHandler? keyboardHandler,
        ITerminalClipboardService? clipboardService,
        ITerminalConnectionHandler? connectionHandler)
    {
        InitializeComponent();

        // Initialize services (use defaults if not injected)
        _keyboardHandler = keyboardHandler ?? new TerminalKeyboardHandler();
        _clipboardService = clipboardService ?? new TerminalClipboardService();
        _connectionHandler = connectionHandler ?? new TerminalConnectionHandler();

        // Initialize output buffer for search with default values
        _outputBuffer = new TerminalOutputBuffer(maxLines: 10000, maxLinesInMemory: 5000);

        // Try to get logger from DI if available
        TryInitializeLogger();

        // Wire up find overlay events
        FindOverlay.CloseRequested += FindOverlay_CloseRequested;
        FindOverlay.NavigateToLine += FindOverlay_NavigateToLine;
        FindOverlay.SearchResultsChanged += FindOverlay_SearchResultsChanged;

        // Wire up terminal events
        TerminalHost.TerminalReady += OnTerminalReady;
        TerminalHost.InputReceived += OnTerminalInputReceived;
        TerminalHost.TerminalResized += OnTerminalResized;
        TerminalHost.FocusChanged += OnTerminalFocusChanged;
        TerminalHost.DataWritten += OnTerminalDataWritten;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void TryInitializeLogger()
    {
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

    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize search service with output buffer
        _searchService ??= new TerminalTextSearchService(_outputBuffer);
        FindOverlay.SetSearchService(_searchService);

        // Restart stats collector if we have an active session
        if (_session?.IsConnected == true && _statsCollector != null && !_statsCollector.IsRunning)
        {
            if (_bridge != null)
            {
                _statsCollector.Start(_session, _bridge);
            }
        }

        _logger.LogDebug("SshTerminalControl loaded");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Pause stats updates while not visible (saves resources)
        _statsCollector?.Stop();
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

        // Record user input to session recorder (if recording is active)
        _session?.SessionRecorder?.RecordInput(input);

        // If broadcast mode is enabled, send to all selected sessions
        if (_broadcastService?.IsEnabled == true)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            _broadcastService.SendToSelected(bytes);
        }
        else if (_serialBridge != null)
        {
            // Send to serial bridge if this is a serial connection
            _serialBridge.SendText(input);
        }
        else
        {
            // Send to SSH bridge for SSH connections
            _bridge?.SendText(input);
        }
    }

    private void OnTerminalResized(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0) return;
        if (cols == _lastColumns && rows == _lastRows) return;

        _lastColumns = cols;
        _lastRows = rows;

        // Record terminal resize to session recorder (if recording is active)
        _session?.SessionRecorder?.RecordResize(cols, rows);

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

    private void OnTerminalFocusChanged(bool hasFocus)
    {
        // Notify focus tracker if available
        if (_focusTracker != null && _session != null)
        {
            var sessionId = _session.Id.ToString();
            if (hasFocus)
            {
                _focusTracker.NotifyFocusGained(sessionId);
                _logger.LogDebug("Terminal focus gained for session {SessionId}", sessionId);
            }
            else
            {
                _focusTracker.NotifyFocusLost(sessionId);
                _logger.LogDebug("Terminal focus lost for session {SessionId}", sessionId);
            }
        }

        // Raise FocusReceived event so parent controls can update pane focus state
        // This is more reliable than WPF's routed GotFocus event for WebView2-based controls
        if (hasFocus)
        {
            FocusReceived?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTerminalDataWritten(string preview)
    {
        // Update session's last output preview for tooltip display
        if (_session != null)
        {
            _session.LastOutputPreview = preview;
        }
    }

    /// <summary>
    /// Handles data received from SSH and displays it in the terminal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Data flow:</b> SSH.NET ShellStream → SshTerminalBridge.DataReceived → here → WebTerminalBridge → xterm.js
    /// </para>
    /// <para>
    /// This method is called on a background thread from SshTerminalBridge's read loop.
    /// However, we don't need to dispatch to the UI thread here because:
    /// 1. TerminalOutputBuffer is thread-safe
    /// 2. WebTerminalBridge.WriteData handles UI dispatch internally
    /// </para>
    /// </remarks>
    private void OnSshDataReceived(byte[] data)
    {
        if (data.Length == 0) return;

        // Record raw SSH output to session recorder (if recording is active)
        _session?.SessionRecorder?.RecordOutput(data);

        // CRITICAL: Use stateful decoder to handle multi-byte UTF-8 sequences correctly.
        // SSH data arrives in arbitrary chunks that may split UTF-8 characters.
        // Example: Chinese character 中 (U+4E2D) = bytes E4 B8 AD
        // If packet 1 contains [E4 B8] and packet 2 contains [AD ...], a stateless
        // decoder would produce garbage. The stateful decoder remembers partial sequences.
        var text = DecodeUtf8(data);

        if (string.IsNullOrEmpty(text)) return;

        // Capture output for search and session export functionality
        _outputBuffer.AppendOutput(text);

        // Write to WebTerminal - the bridge handles batching and UI thread dispatch
        TerminalHost.WriteData(text);
    }

    #region Keyboard Handling

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_keyboardHandler.HandleKeyDown(e, this))
        {
            e.Handled = true;
        }
    }

    #endregion

    #region IKeyboardHandlerContext Implementation

    void IKeyboardHandlerContext.SendText(string text)
    {
        if (_serialBridge != null)
        {
            _serialBridge.SendText(text);
        }
        else
        {
            _bridge?.SendText(text);
        }
    }

    void IKeyboardHandlerContext.ShowFindOverlay() => ShowFindOverlay();

    void IKeyboardHandlerContext.HideFindOverlay() => HideFindOverlay();

    bool IKeyboardHandlerContext.IsFindOverlayVisible => FindOverlay.Visibility == Visibility.Visible;

    void IKeyboardHandlerContext.CopyToClipboard() => CopyToClipboard();

    void IKeyboardHandlerContext.PasteFromClipboard() => PasteFromClipboard();

    void IKeyboardHandlerContext.ZoomIn() => TerminalHost.ZoomIn();

    void IKeyboardHandlerContext.ZoomOut() => TerminalHost.ZoomOut();

    void IKeyboardHandlerContext.ResetZoom() => TerminalHost.ResetZoom();

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
        _clipboardService.CopyToClipboard();
    }

    /// <summary>
    /// Pastes text from the clipboard to the terminal.
    /// </summary>
    public void PasteFromClipboard()
    {
        _clipboardService.PasteFromClipboard(text =>
        {
            if (_serialBridge != null)
            {
                _serialBridge.SendText(text);
            }
            else
            {
                _bridge?.SendText(text);
            }
        });
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

            // Establish SSH connection via handler
            var result = await _connectionHandler.ConnectAsync(
                sshService,
                connectionInfo,
                hostKeyCallback,
                kbInteractiveCallback,
                (uint)_lastColumns,
                (uint)_lastRows,
                cancellationToken);

            // Update session with connection
            if (_session != null)
            {
                _session.Connection = result.Connection;
                // Subscribe to connection-level disconnection for faster detection when host shuts down
                result.Connection.Disconnected += OnConnectionDisconnected;
            }

            // Wire up bridge
            _bridge = result.Bridge;
            _bridge.DataReceived += OnSshDataReceived;
            _bridge.Disconnected += OnBridgeDisconnected;
            _ownsBridge = true;

            // Store bridge in session for mirrored panes to reuse
            if (_session != null)
            {
                _session.Bridge = _bridge;
            }

            // Start reading SSH data
            _bridge.StartReading();

            HideStatus();
            StartStatsCollection();

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

            // Establish SSH connection through proxy chain via handler
            var result = await _connectionHandler.ConnectWithProxyChainAsync(
                sshService,
                connectionChain,
                hostKeyCallback,
                kbInteractiveCallback,
                (uint)_lastColumns,
                (uint)_lastRows,
                cancellationToken);

            // Update session with connection
            if (_session != null)
            {
                _session.Connection = result.Connection;
                // Subscribe to connection-level disconnection for faster detection when host shuts down
                result.Connection.Disconnected += OnConnectionDisconnected;
            }

            // Wire up bridge
            _bridge = result.Bridge;
            _bridge.DataReceived += OnSshDataReceived;
            _bridge.Disconnected += OnBridgeDisconnected;
            _ownsBridge = true;

            // Store bridge in session for mirrored panes to reuse
            if (_session != null)
            {
                _session.Bridge = _bridge;
            }

            // Start reading SSH data
            _bridge.StartReading();

            HideStatus();
            StartStatsCollection();

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
    /// Connects to a serial port and initializes the terminal.
    /// </summary>
    /// <param name="serialService">The serial connection service.</param>
    /// <param name="connectionInfo">Serial port connection parameters.</param>
    /// <param name="session">The terminal session to associate with the connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectSerialAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        TerminalSession session,
        CancellationToken cancellationToken = default)
    {
        if (serialService == null)
        {
            throw new ArgumentNullException(nameof(serialService));
        }

        if (connectionInfo == null)
        {
            throw new ArgumentNullException(nameof(connectionInfo));
        }

        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        // Reset disconnect tracking for new connection
        _disconnectedRaised = false;
        _reconnectAttemptCount = 0;

        _session = session;

        // Store for reconnection
        _serialService = serialService;
        _lastSerialConnectionInfo = connectionInfo;

        try
        {
            ShowStatus("Connecting to serial port...");

            // Initialize WebTerminalControl
            await TerminalHost.InitializeAsync();

            // Connect to serial port
            var connection = await serialService.ConnectAsync(connectionInfo, cancellationToken);

            // Create bridge
            _serialBridge = new SerialTerminalBridge(
                connection.BaseStream,
                logger: null,
                localEcho: connectionInfo.LocalEcho,
                lineEnding: connectionInfo.LineEnding);

            // Store in session
            session.SerialConnection = connection;
            session.SerialBridge = _serialBridge;

            // Wire up events
            _serialBridge.DataReceived += OnSerialDataReceived;
            _serialBridge.Disconnected += OnSerialBridgeDisconnected;

            // Start reading
            _serialBridge.StartReading();

            _ownsBridge = true;

            HideStatus();
            ShowSerialControls();
            StartSerialStatsCollection();
            _logger.LogInformation("Connected to serial port {Port}", connectionInfo.PortName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to serial port {Port}", connectionInfo.PortName);
            ShowStatus($"Connection failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Handles data received from serial port and displays it in the terminal.
    /// </summary>
    private void OnSerialDataReceived(byte[] data)
    {
        if (data.Length == 0) return;

        // Record raw serial output to session recorder (if recording is active)
        _session?.SessionRecorder?.RecordOutput(data);

        // Decode UTF-8 using the stateful decoder (same as SSH data handling)
        var text = DecodeUtf8(data);

        if (string.IsNullOrEmpty(text)) return;

        // Capture output for search and session export functionality
        _outputBuffer.AppendOutput(text);

        // Write to WebTerminal - the bridge handles batching and UI thread dispatch
        TerminalHost.WriteData(text);
    }

    /// <summary>
    /// Handles serial bridge disconnection.
    /// </summary>
    private void OnSerialBridgeDisconnected()
    {
        Dispatcher.Invoke(async () =>
        {
            StopStatsCollection();
            HideSerialControls();
            ShowStatus("Disconnected");
            _logger.LogInformation("Serial connection disconnected for session: {Title}", _session?.Title);

            // Attempt auto-reconnect if enabled
            if (_autoReconnectEnabled && !_isReconnecting && _reconnectAttemptCount < _maxReconnectAttempts)
            {
                _reconnectAttemptCount++;
                _logger.LogInformation("Auto-reconnecting to serial port (attempt {Attempt}/{Max})",
                    _reconnectAttemptCount, _maxReconnectAttempts);

                await Task.Delay(_reconnectDelay);
                await TryAutoReconnectSerialAsync();
                return;
            }

            // Raise the Disconnected event to notify parent controls (only once)
            if (!_disconnectedRaised)
            {
                _disconnectedRaised = true;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>
    /// Attempts to auto-reconnect to the serial port.
    /// </summary>
    private async Task TryAutoReconnectSerialAsync()
    {
        if (_serialService == null || _lastSerialConnectionInfo == null || _session == null)
        {
            _logger.LogWarning("Cannot auto-reconnect: missing service or connection info");
            return;
        }

        try
        {
            await ReconnectSerialAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-reconnect attempt {Attempt} failed", _reconnectAttemptCount);

            // If we have more attempts remaining, the next disconnect will trigger another try
            if (_reconnectAttemptCount >= _maxReconnectAttempts)
            {
                ShowStatus($"Reconnection failed after {_maxReconnectAttempts} attempts");
                if (!_disconnectedRaised)
                {
                    _disconnectedRaised = true;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    /// <summary>
    /// Reconnects to the serial port using stored connection info.
    /// </summary>
    public async Task ReconnectSerialAsync()
    {
        if (_serialService == null || _lastSerialConnectionInfo == null || _session == null)
        {
            throw new InvalidOperationException("Cannot reconnect: missing service, connection info, or session");
        }

        _isReconnecting = true;
        try
        {
            ShowStatus("Reconnecting to serial port...");

            // Cleanup old bridge if present
            if (_serialBridge != null)
            {
                _serialBridge.DataReceived -= OnSerialDataReceived;
                _serialBridge.Disconnected -= OnSerialBridgeDisconnected;
                _serialBridge.Dispose();
                _serialBridge = null;
            }

            // Cleanup old connection if present
            if (_session.SerialConnection != null)
            {
                _session.SerialConnection.Dispose();
                _session.SerialConnection = null;
            }

            // Connect to serial port
            var connection = await _serialService.ConnectAsync(_lastSerialConnectionInfo, CancellationToken.None);

            // Create bridge
            _serialBridge = new SerialTerminalBridge(
                connection.BaseStream,
                logger: null,
                localEcho: _lastSerialConnectionInfo.LocalEcho,
                lineEnding: _lastSerialConnectionInfo.LineEnding);

            // Store in session
            _session.SerialConnection = connection;
            _session.SerialBridge = _serialBridge;

            // Wire up events
            _serialBridge.DataReceived += OnSerialDataReceived;
            _serialBridge.Disconnected += OnSerialBridgeDisconnected;

            // Start reading
            _serialBridge.StartReading();

            _ownsBridge = true;
            _reconnectAttemptCount = 0;
            _disconnectedRaised = false;

            HideStatus();
            ShowSerialControls();
            StartSerialStatsCollection();
            _logger.LogInformation("Reconnected to serial port {Port}", _lastSerialConnectionInfo.PortName);

            // Raise reconnect success event
            ReconnectSucceeded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isReconnecting = false;
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

            // Use handler to attach to session
            var attachResult = _connectionHandler.AttachToSession(_session);

            if (attachResult.Bridge != null)
            {
                _bridge = attachResult.Bridge;
                _bridge.DataReceived += OnSshDataReceived;
                _ownsBridge = attachResult.OwnsBridge;

                if (attachResult.OwnsBridge)
                {
                    _bridge.Disconnected += OnBridgeDisconnected;

                    // Subscribe to connection-level disconnection for faster detection when host shuts down
                    if (_session.Connection != null)
                    {
                        _session.Connection.Disconnected += OnConnectionDisconnected;
                    }
                }

                if (attachResult.NeedsStartReading)
                {
                    _bridge.StartReading();
                }

                _logger.LogDebug("Attached to session: {Title}", session.Title);
            }

            HideStatus();
            StartStatsCollection();
        }
        else
        {
            StopStatsCollection();
            ShowStatus("Disconnected");
        }
    }

    /// <summary>
    /// Disconnects from the SSH server and cleans up resources.
    /// </summary>
    public void Disconnect()
    {
        StopStatsCollection();
        HideSerialControls();

        if (_bridge != null)
        {
            _bridge.DataReceived -= OnSshDataReceived;
            _bridge.Disconnected -= OnBridgeDisconnected;

            _connectionHandler.Disconnect(_bridge, _ownsBridge);
            _bridge = null;
        }

        // Dispose serial bridge if present
        if (_serialBridge != null)
        {
            _serialBridge.DataReceived -= OnSerialDataReceived;
            _serialBridge.Disconnected -= OnSerialBridgeDisconnected;

            if (_ownsBridge)
            {
                _serialBridge.Dispose();
            }
            _serialBridge = null;
        }

        // Dispose serial connection if present
        if (_session?.SerialConnection != null)
        {
            _session.SerialConnection.Dispose();
        }

        if (_session?.Connection != null)
        {
            _session.Connection.Disconnected -= OnConnectionDisconnected;
            _session.Connection.Dispose();
        }

        // Clear the output buffer to free memory and cleanup temp files
        _outputBuffer.Clear();

        ShowStatus("Disconnected");
    }

    private void OnConnectionDisconnected(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            StopStatsCollection();
            ShowStatus("Disconnected");
            _logger.LogInformation("SSH connection disconnected (connection-level) for session: {Title}", _session?.Title);

            // Raise the Disconnected event to notify parent controls (only once)
            if (!_disconnectedRaised)
            {
                _disconnectedRaised = true;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnBridgeDisconnected(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            StopStatsCollection();
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

    #region Stats Collection

    private void StartStatsCollection()
    {
        if (_session == null) return;

        // For SSH connections, require a bridge
        if (_bridge == null && _serialBridge == null) return;

        // Create stats collector if needed
        _statsCollector ??= new TerminalStatsCollector(_serverStatsService);
        _statsCollector.StatsUpdated += OnStatsUpdated;

        // Start stats collection - for SSH, use the bridge; for serial, pass null bridge
        if (_bridge != null)
        {
            _statsCollector.Start(_session, _bridge);
        }
        else
        {
            // For serial connections, start stats without SSH bridge
            _statsCollector.Start(_session, null);
        }

        // Configure status bar based on connection type
        StatusBar.Stats = _session.Stats;
        StatusBar.IsSerialSession = _session.IsSerialSession;

        if (_session.IsSerialSession && _lastSerialConnectionInfo != null)
        {
            StatusBar.SerialConnectionInfo = _lastSerialConnectionInfo;
        }

        StatusBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Starts stats collection specifically for serial sessions.
    /// Call this after successful serial connection.
    /// </summary>
    private void StartSerialStatsCollection()
    {
        if (_session == null || _serialBridge == null) return;

        // Create stats collector if needed
        _statsCollector ??= new TerminalStatsCollector(_serverStatsService);
        _statsCollector.StatsUpdated += OnStatsUpdated;

        // Start stats without SSH bridge (serial doesn't use SSH)
        _statsCollector.Start(_session, null);

        // Configure status bar for serial connection
        StatusBar.Stats = _session.Stats;
        StatusBar.IsSerialSession = true;

        if (_lastSerialConnectionInfo != null)
        {
            StatusBar.SerialConnectionInfo = _lastSerialConnectionInfo;
        }

        StatusBar.Visibility = Visibility.Visible;
    }

    private void StopStatsCollection()
    {
        if (_statsCollector != null)
        {
            _statsCollector.StatsUpdated -= OnStatsUpdated;
            _statsCollector.Stop();
        }
    }

    private void OnStatsUpdated(object? sender, TerminalStats stats)
    {
        StatusBar.Stats = stats;
        StatusBar.UpdateDisplay();
    }

    #endregion

    #region Reconnection Properties

    /// <summary>
    /// Gets whether reconnection is possible for the current session.
    /// </summary>
    public bool CanReconnect =>
        _session?.Host != null &&
        !IsConnected &&
        !_isReconnecting &&
        (_session.Host.ConnectionType == ConnectionType.Serial
            ? _serialService != null && _lastSerialConnectionInfo != null
            : true); // SSH reconnection would have its own check

    /// <summary>
    /// Gets or sets whether auto-reconnect is enabled for this terminal.
    /// When enabled, the terminal will automatically attempt to reconnect after disconnection.
    /// </summary>
    public bool AutoReconnectEnabled
    {
        get => _autoReconnectEnabled;
        set
        {
            if (_autoReconnectEnabled != value)
            {
                _autoReconnectEnabled = value;
                OnPropertyChanged();
                _logger.LogDebug("Auto-reconnect {State}", value ? "enabled" : "disabled");
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts.
    /// </summary>
    public int MaxReconnectAttempts
    {
        get => _maxReconnectAttempts;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Must be non-negative");
            if (_maxReconnectAttempts != value)
            {
                _maxReconnectAttempts = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the number of reconnection attempts made since the last successful connection.
    /// </summary>
    public int ReconnectAttemptCount => _reconnectAttemptCount;

    /// <summary>
    /// Gets whether a reconnection attempt is currently in progress.
    /// </summary>
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// Resets the reconnection attempt counter.
    /// </summary>
    public void ResetReconnectAttempts()
    {
        _reconnectAttemptCount = 0;
    }

    /// <summary>
    /// Event raised when reconnection succeeds.
    /// </summary>
    public event EventHandler? ReconnectSucceeded;

    /// <summary>
    /// Configures auto-reconnect settings from application settings.
    /// </summary>
    /// <param name="enabled">Whether auto-reconnect is enabled.</param>
    /// <param name="maxAttempts">Maximum number of reconnection attempts.</param>
    public void ConfigureAutoReconnect(bool enabled, int maxAttempts = 3)
    {
        _autoReconnectEnabled = enabled;
        _maxReconnectAttempts = maxAttempts;
        _logger.LogDebug("Auto-reconnect configured: enabled={Enabled}, maxAttempts={MaxAttempts}",
            enabled, maxAttempts);
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

    #region Serial Controls

    /// <summary>
    /// Gets whether DTR (Data Terminal Ready) signal is currently enabled.
    /// </summary>
    public bool IsDtrEnabled => _session?.Host?.SerialDtrEnable ?? true;

    /// <summary>
    /// Gets whether RTS (Request To Send) signal is currently enabled.
    /// </summary>
    public bool IsRtsEnabled => _session?.Host?.SerialRtsEnable ?? true;

    /// <summary>
    /// Command to toggle the DTR signal on serial connections.
    /// </summary>
    public ICommand ToggleDtrCommand => _toggleDtrCommand ??= new RelayCommand(
        () => SetDtr(!IsDtrEnabled),
        () => _session?.SerialConnection?.IsConnected == true);

    /// <summary>
    /// Command to toggle the RTS signal on serial connections.
    /// </summary>
    public ICommand ToggleRtsCommand => _toggleRtsCommand ??= new RelayCommand(
        () => SetRts(!IsRtsEnabled),
        () => _session?.SerialConnection?.IsConnected == true);

    /// <summary>
    /// Command to send a break signal on serial connections.
    /// </summary>
    public ICommand SendBreakCommand => _sendBreakCommand ??= new RelayCommand(
        SendBreak,
        () => _session?.SerialConnection?.IsConnected == true);

    /// <summary>
    /// Command to toggle local echo for serial connections.
    /// </summary>
    public ICommand ToggleLocalEchoCommand => _toggleLocalEchoCommand ??= new RelayCommand(
        ToggleLocalEcho,
        () => _session?.SerialConnection?.IsConnected == true);

    /// <summary>
    /// Sets the DTR (Data Terminal Ready) signal state.
    /// </summary>
    /// <param name="enabled">True to enable DTR, false to disable.</param>
    public void SetDtr(bool enabled)
    {
        if (_session?.SerialConnection != null)
        {
            try
            {
                _session.SerialConnection.SetDtr(enabled);
                if (_session.Host != null)
                {
                    _session.Host.SerialDtrEnable = enabled;
                }
                OnPropertyChanged(nameof(IsDtrEnabled));
                _logger.LogInformation("DTR signal set to {State}", enabled ? "enabled" : "disabled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set DTR signal");
            }
        }
    }

    /// <summary>
    /// Sets the RTS (Request To Send) signal state.
    /// </summary>
    /// <param name="enabled">True to enable RTS, false to disable.</param>
    public void SetRts(bool enabled)
    {
        if (_session?.SerialConnection != null)
        {
            try
            {
                _session.SerialConnection.SetRts(enabled);
                if (_session.Host != null)
                {
                    _session.Host.SerialRtsEnable = enabled;
                }
                OnPropertyChanged(nameof(IsRtsEnabled));
                _logger.LogInformation("RTS signal set to {State}", enabled ? "enabled" : "disabled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set RTS signal");
            }
        }
    }

    /// <summary>
    /// Sends a break signal on the serial connection (250ms duration).
    /// </summary>
    public void SendBreak()
    {
        if (_session?.SerialConnection != null)
        {
            try
            {
                _session.SerialConnection.SendBreak(250);
                _logger.LogInformation("Sent break signal (250ms)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send break signal");
            }
        }
    }

    /// <summary>
    /// Notifies the UI that serial connection state has changed.
    /// Call this after connecting or disconnecting to update command states.
    /// </summary>
    public void NotifySerialStateChanged()
    {
        OnPropertyChanged(nameof(IsSerialConnected));
        OnPropertyChanged(nameof(IsDtrEnabled));
        OnPropertyChanged(nameof(IsRtsEnabled));
        OnPropertyChanged(nameof(IsLocalEchoEnabled));

        // Re-evaluate command CanExecute states
        (_toggleDtrCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (_toggleRtsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (_sendBreakCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (_toggleLocalEchoCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void LocalEchoCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        IsLocalEchoEnabled = true;
    }

    private void LocalEchoCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        IsLocalEchoEnabled = false;
    }

    private void DtrToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        SetDtr(true);
    }

    private void DtrToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        SetDtr(false);
    }

    private void RtsToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        SetRts(true);
    }

    private void RtsToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        SetRts(false);
    }

    private void SendBreakButton_Click(object sender, RoutedEventArgs e)
    {
        SendBreak();
    }

    private void ShowSerialControls()
    {
        SerialControlsPanel.Visibility = Visibility.Visible;
        LocalEchoCheckBox.IsChecked = IsLocalEchoEnabled;

        // Initialize DTR/RTS toggle states from host settings
        if (_session?.Host != null)
        {
            DtrToggleButton.IsChecked = _session.Host.SerialDtrEnable;
            RtsToggleButton.IsChecked = _session.Host.SerialRtsEnable;
        }

        NotifySerialStateChanged();
    }

    private void HideSerialControls()
    {
        SerialControlsPanel.Visibility = Visibility.Collapsed;
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
    public bool IsConnected => _session?.Connection?.IsConnected == true || _session?.SerialConnection?.IsConnected == true;

    /// <summary>
    /// Gets whether this is an active serial connection.
    /// </summary>
    public bool IsSerialConnected => _serialBridge != null && _session?.SerialConnection?.IsConnected == true;

    /// <summary>
    /// Gets or sets whether local echo is enabled for serial connections.
    /// When enabled, typed characters are echoed back locally instead of waiting for the remote device.
    /// </summary>
    public bool IsLocalEchoEnabled
    {
        get => _serialBridge?.LocalEcho ?? _session?.Host?.SerialLocalEcho ?? false;
        set
        {
            if (_serialBridge != null)
            {
                _serialBridge.LocalEcho = value;
                OnPropertyChanged();
                _logger.LogDebug("Local echo {State} for serial connection", value ? "enabled" : "disabled");
            }
        }
    }

    /// <summary>
    /// Toggles local echo for serial connections.
    /// </summary>
    public void ToggleLocalEcho()
    {
        if (_serialBridge != null)
        {
            IsLocalEchoEnabled = !IsLocalEchoEnabled;
        }
    }

    /// <summary>
    /// Gets or sets the scrollback buffer size (total lines across all segments).
    /// </summary>
    public int ScrollbackBufferSize
    {
        get => _outputBuffer.MaxLines;
        set => _outputBuffer.MaxLines = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of lines to keep in memory.
    /// Older lines are compressed and archived to disk.
    /// </summary>
    public int MaxLinesInMemory
    {
        get => _outputBuffer.MaxLinesInMemory;
        set => _outputBuffer.MaxLinesInMemory = value;
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
    /// Event raised when the terminal receives focus (e.g., user clicks on the terminal).
    /// This is more reliable than WPF's GotFocus event for WebView2-based controls.
    /// </summary>
    public event EventHandler? FocusReceived;

    /// <summary>
    /// Focuses the terminal input.
    /// </summary>
    public void FocusInput()
    {
        // Explicitly call the WebTerminalControl's custom Focus method
        // which handles WebView2 focus properly
        ((WebTerminalControl)TerminalHost).Focus();
    }

    /// <summary>
    /// Sends a command string to the terminal, followed by a carriage return.
    /// </summary>
    public void SendCommand(string command)
    {
        if (_serialBridge != null)
        {
            _serialBridge.SendCommand(command);
        }
        else
        {
            _bridge?.SendCommand(command);
        }
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
    /// Sets the terminal focus tracker service for reliable keyboard shortcut handling.
    /// When set, the control will notify the tracker when it gains/loses focus,
    /// allowing the main window to correctly route keyboard shortcuts.
    /// </summary>
    public void SetFocusTracker(ITerminalFocusTracker? tracker)
    {
        _focusTracker = tracker;
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

        var fontStack = FontStackBuilder.Build(fontFamily);
        TerminalHost.SetFont(fontStack, fontSize);
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
    /// Requests a terminal fit/refresh operation.
    /// Call this when the terminal becomes visible after being hidden,
    /// as WebView2 controls may not properly repaint after visibility changes.
    /// </summary>
    public void RefreshTerminal()
    {
        // Use RefreshVisual which invalidates and triggers a fit for proper repaint
        TerminalHost.RefreshVisual();
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
