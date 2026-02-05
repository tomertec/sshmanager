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
using SshManager.Terminal.Services.Connection;
using SshManager.Terminal.Services.Lifecycle;
using SshManager.Terminal.Services.Stats;
using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Controls;

/// <summary>
/// WPF terminal control for SSH and serial sessions using WebTerminalControl (xterm.js + WebView2).
/// This control delegates to extracted services for lifecycle, connection, stats, and theming.
/// </summary>
public partial class SshTerminalControl : UserControl, IKeyboardHandlerContext, INotifyPropertyChanged
{
    private ILogger<SshTerminalControl> _logger = NullLogger<SshTerminalControl>.Instance;

    // Extracted services for single responsibility
    private readonly ITerminalKeyboardHandler _keyboardHandler;
    private readonly ITerminalClipboardService _clipboardService;
    private readonly ITerminalConnectionHandler _connectionHandler;
    private readonly ISshSessionConnector _sshConnector;
    private readonly ISerialSessionConnector _serialConnector;
    private readonly ITerminalSessionLifecycle _sessionLifecycle;
    private readonly ITerminalStatsCoordinator _statsCoordinator;
    private readonly ITerminalThemeManager _themeManager;

    // Optional services injected at runtime
    private IBroadcastInputService? _broadcastService;
    private ITerminalFocusTracker? _focusTracker;

    // Serial reconnection state
    private ISerialConnectionService? _serialService;
    private SerialConnectionInfo? _lastSerialConnectionInfo;
    private bool _autoReconnectEnabled;
    private int _maxReconnectAttempts = 3;
    private int _reconnectAttemptCount;
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(2);
    private bool _isReconnecting;

    // Output buffer for search
    private readonly TerminalOutputBuffer _outputBuffer;
    private TerminalTextSearchService? _searchService;

    // UTF-8 stateful decoding for multi-byte sequences split across packets
    private readonly Utf8DecoderHelper _decoderHelper = new();

    // Resize tracking to avoid spamming server
    private int _lastColumns = 80;
    private int _lastRows = 24;

    // Disconnect tracking to prevent duplicate events
    private bool _disconnectedRaised;
    private bool _isPrimaryPane = true;

    // Serial control commands (lazy initialized)
    private ICommand? _toggleDtrCommand;
    private ICommand? _toggleRtsCommand;
    private ICommand? _sendBreakCommand;
    private ICommand? _toggleLocalEchoCommand;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Disconnected;
    public event EventHandler? FocusReceived;
    public event EventHandler? ReconnectSucceeded;

    public SshTerminalControl() : this(null, null, null, null, null, null, null, null) { }

    public SshTerminalControl(
        ITerminalKeyboardHandler? keyboardHandler,
        ITerminalClipboardService? clipboardService,
        ITerminalConnectionHandler? connectionHandler,
        ISshSessionConnector? sshConnector = null,
        ISerialSessionConnector? serialConnector = null,
        ITerminalSessionLifecycle? sessionLifecycle = null,
        ITerminalStatsCoordinator? statsCoordinator = null,
        ITerminalThemeManager? themeManager = null)
    {
        InitializeComponent();

        _keyboardHandler = keyboardHandler ?? new TerminalKeyboardHandler();
        _clipboardService = clipboardService ?? new TerminalClipboardService();
        _connectionHandler = connectionHandler ?? new TerminalConnectionHandler();
        _sshConnector = sshConnector ?? new SshSessionConnector();
        _serialConnector = serialConnector ?? new SerialSessionConnector();
        _sessionLifecycle = sessionLifecycle ?? new TerminalSessionLifecycle();
        _statsCoordinator = statsCoordinator ?? new TerminalStatsCoordinator();
        _themeManager = themeManager ?? new TerminalThemeManager();

        _outputBuffer = new TerminalOutputBuffer(maxLines: 10000, maxLinesInMemory: 5000);

        TryInitializeLogger();
        WireEvents();
    }

    private void TryInitializeLogger()
    {
        try
        {
            var loggerFactory = Application.Current?.TryFindResource("ILoggerFactory") as ILoggerFactory;
            if (loggerFactory != null)
                _logger = loggerFactory.CreateLogger<SshTerminalControl>();
        }
        catch
        {
            // Intentionally swallowing: Application resources may not be available
            // during designer-time or if resources fail to load
            // This is safe because the logger is optional and the control works without it
        }
    }

    private void WireEvents()
    {
        FindOverlay.CloseRequested += (_, _) => HideFindOverlay();
        FindOverlay.NavigateToLine += (_, lineIndex) => _logger.LogDebug("Navigate to line {LineIndex}", lineIndex);
        FindOverlay.SearchResultsChanged += (_, _) => { };

        TerminalHost.TerminalReady += OnTerminalReady;
        TerminalHost.InputReceived += OnTerminalInputReceived;
        TerminalHost.TerminalResized += OnTerminalResized;
        TerminalHost.FocusChanged += OnTerminalFocusChanged;
        TerminalHost.DataWritten += OnTerminalDataWritten;

        _sshConnector.Disconnected += OnConnectorDisconnected;
        _serialConnector.Disconnected += OnConnectorDisconnected;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region Lifecycle Events

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _searchService ??= new TerminalTextSearchService(_outputBuffer);
        FindOverlay.SetSearchService(_searchService);

        if (_sessionLifecycle.IsConnected && !_statsCoordinator.IsCollecting)
            _statsCoordinator.Resume();

        _logger.LogDebug("SshTerminalControl loaded");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _statsCoordinator.Pause();
        // Do NOT dispose _outputBuffer here. WPF fires Unloaded when switching tabs,
        // which would destroy the buffer and cause ObjectDisposedException on tab switch back.
        // Buffer is disposed via DisposeTerminal() when the session is permanently closed.
        _logger.LogDebug("SshTerminalControl unloaded");
    }

    #endregion

    #region Terminal Events

    private void OnTerminalReady()
    {
        _logger.LogDebug("WebTerminalControl ready");
        if (_themeManager.CurrentTheme != null)
            _themeManager.ApplyTheme(_themeManager.CurrentTheme, TerminalHost);
        _themeManager.ApplyFontSettings(TerminalHost);
        TerminalHost.Focus();
    }

    private void OnTerminalInputReceived(string input)
    {
        if (string.IsNullOrEmpty(input)) return;
        _sessionLifecycle.CurrentSession?.SessionRecorder?.RecordInput(input);
        if (_broadcastService?.IsEnabled == true)
            _broadcastService.SendToSelected(Encoding.UTF8.GetBytes(input));
        else
            SendTextToBridge(input);
    }

    private void OnTerminalResized(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0 || (cols == _lastColumns && rows == _lastRows)) return;

        _lastColumns = cols;
        _lastRows = rows;
        _sessionLifecycle.CurrentSession?.SessionRecorder?.RecordResize(cols, rows);

        var connection = _sessionLifecycle.CurrentSession?.Connection;
        if (connection?.ResizeTerminal((uint)cols, (uint)rows) == true)
            _logger.LogDebug("Terminal resized to {Cols}x{Rows}", cols, rows);
    }

    private void OnTerminalFocusChanged(bool hasFocus)
    {
        var session = _sessionLifecycle.CurrentSession;
        if (_focusTracker != null && session != null)
        {
            var sessionId = session.Id.ToString();
            if (hasFocus)
                _focusTracker.NotifyFocusGained(sessionId);
            else
                _focusTracker.NotifyFocusLost(sessionId);
        }

        if (hasFocus)
            FocusReceived?.Invoke(this, EventArgs.Empty);
    }

    private void OnTerminalDataWritten(string preview)
    {
        if (_sessionLifecycle.CurrentSession != null)
            _sessionLifecycle.CurrentSession.LastOutputPreview = preview;
    }

    private void OnConnectorDisconnected(object? sender, EventArgs e)
    {
        // Use InvokeAsync instead of Invoke to avoid blocking the background thread
        // and to properly handle the async lambda (Invoke with async lambda returns
        // at the first await, which is misleading).
        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                _statsCoordinator.Stop();
                HideSerialControls();
                ShowStatus("Disconnected");
                _logger.LogInformation("Connection disconnected for session: {Title}", _sessionLifecycle.CurrentSession?.Title);

                if (_autoReconnectEnabled && !_isReconnecting && _reconnectAttemptCount < _maxReconnectAttempts &&
                    _sessionLifecycle.CurrentSession?.Host?.ConnectionType == ConnectionType.Serial)
                {
                    _reconnectAttemptCount++;
                    await Task.Delay(_reconnectDelay);
                    await TryAutoReconnectSerialAsync();
                    return;
                }

                if (!_disconnectedRaised)
                {
                    _disconnectedRaised = true;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnConnectorDisconnected: {ex}");
            }
        });
    }

    #endregion

    #region Data Handling

    private void OnSshDataReceived(byte[] data) => HandleDataReceived(data);
    private void OnSerialDataReceived(byte[] data) => HandleDataReceived(data);

    private void HandleDataReceived(byte[] data)
    {
        if (data.Length == 0) return;
        _sessionLifecycle.CurrentSession?.SessionRecorder?.RecordOutput(data);

        var text = _decoderHelper.Decode(data, 0, data.Length);
        if (!string.IsNullOrEmpty(text))
        {
            _outputBuffer.AppendOutput(text);
            TerminalHost.WriteData(text);
        }
    }

    #endregion

    #region Connection Management

    public Task ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback = null,
        CancellationToken cancellationToken = default)
        => ConnectAsync(sshService, connectionInfo, hostKeyCallback, null, cancellationToken);

    public async Task ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        _disconnectedRaised = false;
        var session = DataContext as TerminalSession;

        try
        {
            ShowStatus("Connecting...");
            await TerminalHost.InitializeAsync();

            var result = await _sshConnector.ConnectAsync(
                sshService, connectionInfo, hostKeyCallback, kbInteractiveCallback,
                (uint)_lastColumns, (uint)_lastRows, cancellationToken);

            _sessionLifecycle.SetSession(session, result.Bridge, true);

            if (session != null)
            {
                session.Connection = result.Connection;
                session.Bridge = result.Bridge;
            }

            _sshConnector.WireBridgeEvents(result.Bridge, OnSshDataReceived);
            result.Bridge.StartReading();

            // Diagnostic: Check if the WebTerminalBridge is ready
            // If not ready, data will be buffered and terminal will appear black
            if (!TerminalHost.IsTerminalReady)
            {
                _logger.LogWarning("Terminal WebView2 bridge not ready yet - SSH data may be buffered. " +
                    "If terminal shows black screen, check: 1) Network/firewall (xterm.js CDN), 2) JavaScript console errors");
            }
            else
            {
                _logger.LogDebug("Terminal WebView2 bridge is ready - data will flow to xterm.js");
            }

            HideStatus();
            if (session != null)
                _statsCoordinator.StartForSshSession(session, result.Bridge, StatusBar);
            else
                _logger.LogWarning("DataContext is not a TerminalSession; skipping stats coordinator");
            _logger.LogInformation("Connected to {Host}", connectionInfo.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to {Host}", connectionInfo.Hostname);
            ShowStatus($"Connection failed: {ex.Message}");
            throw;
        }
    }

    public async Task ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken cancellationToken = default)
    {
        if (connectionChain == null || connectionChain.Count == 0)
            throw new ArgumentException("Connection chain cannot be empty", nameof(connectionChain));

        _disconnectedRaised = false;
        var session = DataContext as TerminalSession;

        try
        {
            ShowStatus("Connecting through proxy chain...");
            await TerminalHost.InitializeAsync();

            var result = await _sshConnector.ConnectWithProxyChainAsync(
                sshService, connectionChain, hostKeyCallback, kbInteractiveCallback,
                (uint)_lastColumns, (uint)_lastRows, cancellationToken);

            _sessionLifecycle.SetSession(session, result.Bridge, true);

            if (session != null)
            {
                session.Connection = result.Connection;
                session.Bridge = result.Bridge;
            }

            _sshConnector.WireBridgeEvents(result.Bridge, OnSshDataReceived);
            result.Bridge.StartReading();

            HideStatus();
            if (session != null)
                _statsCoordinator.StartForSshSession(session, result.Bridge, StatusBar);
            else
                _logger.LogWarning("DataContext is not a TerminalSession; skipping stats coordinator");
            _logger.LogInformation("Connected through proxy chain: {Chain}",
                string.Join(" -> ", connectionChain.Select(c => c.Hostname)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect through proxy chain");
            ShowStatus($"Connection failed: {ex.Message}");
            throw;
        }
    }

    public async Task ConnectSerialAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        TerminalSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serialService);
        ArgumentNullException.ThrowIfNull(connectionInfo);
        ArgumentNullException.ThrowIfNull(session);

        _disconnectedRaised = false;
        _reconnectAttemptCount = 0;
        _serialService = serialService;
        _lastSerialConnectionInfo = connectionInfo;

        try
        {
            ShowStatus("Connecting to serial port...");
            await TerminalHost.InitializeAsync();

            var result = await _serialConnector.ConnectAsync(serialService, connectionInfo, cancellationToken);

            _sessionLifecycle.SetSerialSession(session, result.Bridge, true);
            session.SerialConnection = result.Connection;
            session.SerialBridge = result.Bridge;

            _serialConnector.WireBridgeEvents(result.Bridge, OnSerialDataReceived);
            result.Bridge.StartReading();

            HideStatus();
            ShowSerialControls();
            _statsCoordinator.StartForSerialSession(session, connectionInfo, StatusBar);
            _logger.LogInformation("Connected to serial port {Port}", connectionInfo.PortName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to serial port {Port}", connectionInfo.PortName);
            ShowStatus($"Connection failed: {ex.Message}");
            throw;
        }
    }

    public async Task AttachToSessionAsync(TerminalSession session)
    {
        await _sessionLifecycle.AttachToSessionAsync(session, TerminalHost, _connectionHandler, OnSshDataReceived);

        if (_sessionLifecycle.IsConnected)
        {
            HideStatus();
            _statsCoordinator.StartForSshSession(session, _sessionLifecycle.SshBridge, StatusBar);
        }
        else
        {
            _statsCoordinator.Stop();
            ShowStatus("Disconnected");
        }
    }

    public void Disconnect()
    {
        _statsCoordinator.Stop();
        HideSerialControls();

        // Unwire and cleanup bridges via connectors
        if (_sessionLifecycle.SshBridge != null)
            _sshConnector.Disconnect(_sessionLifecycle.SshBridge, _sessionLifecycle.OwnsBridge);

        if (_sessionLifecycle.SerialBridge != null)
            _serialConnector.Disconnect(_sessionLifecycle.SerialBridge, _sessionLifecycle.OwnsBridge,
                _sessionLifecycle.CurrentSession?.SerialConnection);

        // Cleanup session resources asynchronously to avoid deadlock.
        // CloseAsync uses Dispatcher.Invoke internally (via SessionClosed event),
        // so calling .GetAwaiter().GetResult() from the UI thread would deadlock.
        var session = _sessionLifecycle.CurrentSession;
        if (session != null)
            _ = Task.Run(async () =>
            {
                try { await session.CloseAsync(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Error closing session during disconnect"); }
            });

        _sessionLifecycle.Disconnect();
        _outputBuffer.Clear();
        ShowStatus("Disconnected");
    }

    #endregion

    #region Serial Reconnection

    private async Task TryAutoReconnectSerialAsync()
    {
        if (_serialService == null || _lastSerialConnectionInfo == null || _sessionLifecycle.CurrentSession == null)
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

    public async Task ReconnectSerialAsync()
    {
        var session = _sessionLifecycle.CurrentSession;
        if (_serialService == null || _lastSerialConnectionInfo == null || session == null)
            throw new InvalidOperationException("Cannot reconnect: missing service, connection info, or session");

        _isReconnecting = true;
        try
        {
            ShowStatus("Reconnecting to serial port...");

            // Cleanup old bridge
            if (_sessionLifecycle.SerialBridge != null)
            {
                _serialConnector.UnwireBridgeEvents(_sessionLifecycle.SerialBridge);
                _sessionLifecycle.SerialBridge.Dispose();
            }

            session.SerialConnection?.Dispose();
            session.SerialConnection = null;

            var connection = await _serialService.ConnectAsync(_lastSerialConnectionInfo, CancellationToken.None);
            var bridge = new SerialTerminalBridge(
                connection.BaseStream, logger: null,
                localEcho: _lastSerialConnectionInfo.LocalEcho,
                lineEnding: _lastSerialConnectionInfo.LineEnding);

            session.SerialConnection = connection;
            session.SerialBridge = bridge;
            _sessionLifecycle.SetSerialSession(session, bridge, true);

            _serialConnector.WireBridgeEvents(bridge, OnSerialDataReceived);
            bridge.StartReading();

            _reconnectAttemptCount = 0;
            _disconnectedRaised = false;

            HideStatus();
            ShowSerialControls();
            _statsCoordinator.StartForSerialSession(session, _lastSerialConnectionInfo, StatusBar);
            _logger.LogInformation("Reconnected to serial port {Port}", _lastSerialConnectionInfo.PortName);

            ReconnectSucceeded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isReconnecting = false;
        }
    }

    public bool CanReconnect =>
        _sessionLifecycle.CurrentSession?.Host != null &&
        !IsConnected && !_isReconnecting &&
        (_sessionLifecycle.CurrentSession.Host.ConnectionType == ConnectionType.Serial
            ? _serialService != null && _lastSerialConnectionInfo != null
            : true);

    public bool AutoReconnectEnabled
    {
        get => _autoReconnectEnabled;
        set { if (_autoReconnectEnabled != value) { _autoReconnectEnabled = value; OnPropertyChanged(); } }
    }

    public int MaxReconnectAttempts
    {
        get => _maxReconnectAttempts;
        set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); if (_maxReconnectAttempts != value) { _maxReconnectAttempts = value; OnPropertyChanged(); } }
    }

    public int ReconnectAttemptCount => _reconnectAttemptCount;
    public bool IsReconnecting => _isReconnecting;
    public void ResetReconnectAttempts() => _reconnectAttemptCount = 0;

    public void ConfigureAutoReconnect(bool enabled, int maxAttempts = 3)
    {
        _autoReconnectEnabled = enabled;
        _maxReconnectAttempts = maxAttempts;
    }

    #endregion

    #region Keyboard & Clipboard

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_keyboardHandler.HandleKeyDown(e, this)) e.Handled = true;
    }

    private void SendTextToBridge(string text)
    {
        if (_sessionLifecycle.SerialBridge != null) _sessionLifecycle.SerialBridge.SendText(text);
        else _sessionLifecycle.SshBridge?.SendText(text);
    }

    void IKeyboardHandlerContext.SendText(string text) => SendTextToBridge(text);
    void IKeyboardHandlerContext.ShowFindOverlay() => ShowFindOverlay();
    void IKeyboardHandlerContext.HideFindOverlay() => HideFindOverlay();
    bool IKeyboardHandlerContext.IsFindOverlayVisible => FindOverlay.Visibility == Visibility.Visible;
    void IKeyboardHandlerContext.CopyToClipboard() => CopyToClipboard();
    void IKeyboardHandlerContext.PasteFromClipboard() => PasteFromClipboard();
    void IKeyboardHandlerContext.ZoomIn() => TerminalHost.ZoomIn();
    void IKeyboardHandlerContext.ZoomOut() => TerminalHost.ZoomOut();
    void IKeyboardHandlerContext.ResetZoom() => TerminalHost.ResetZoom();
    bool IKeyboardHandlerContext.IsCompletionPopupVisible => false;
    string IKeyboardHandlerContext.GetCurrentInputLine() => string.Empty;
    int IKeyboardHandlerContext.GetCursorPosition() => 0;
    void IKeyboardHandlerContext.RequestCompletions() { }
    void IKeyboardHandlerContext.InsertCompletion(string text) { }
    void IKeyboardHandlerContext.CompletionSelectPrevious() { }
    void IKeyboardHandlerContext.CompletionSelectNext() { }
    void IKeyboardHandlerContext.AcceptCompletion() { }
    void IKeyboardHandlerContext.HideCompletionPopup() { }

    public void CopyToClipboard() => _clipboardService.CopyToClipboard();
    public void PasteFromClipboard() => _clipboardService.PasteFromClipboard(SendTextToBridge);
    public void ShowFindOverlay() => FindOverlay.Show();
    public void HideFindOverlay() { FindOverlay.Hide(); TerminalHost.Focus(); }

    #endregion

    #region Serial Controls

    public bool IsDtrEnabled => _sessionLifecycle.CurrentSession?.Host?.SerialDtrEnable ?? true;
    public bool IsRtsEnabled => _sessionLifecycle.CurrentSession?.Host?.SerialRtsEnable ?? true;

    public ICommand ToggleDtrCommand => _toggleDtrCommand ??= new RelayCommand(
        () => SetDtr(!IsDtrEnabled),
        () => _sessionLifecycle.CurrentSession?.SerialConnection?.IsConnected == true);

    public ICommand ToggleRtsCommand => _toggleRtsCommand ??= new RelayCommand(
        () => SetRts(!IsRtsEnabled),
        () => _sessionLifecycle.CurrentSession?.SerialConnection?.IsConnected == true);

    public ICommand SendBreakCommand => _sendBreakCommand ??= new RelayCommand(
        SendBreak,
        () => _sessionLifecycle.CurrentSession?.SerialConnection?.IsConnected == true);

    public ICommand ToggleLocalEchoCommand => _toggleLocalEchoCommand ??= new RelayCommand(
        ToggleLocalEcho,
        () => _sessionLifecycle.CurrentSession?.SerialConnection?.IsConnected == true);

    public void SetDtr(bool enabled)
    {
        var session = _sessionLifecycle.CurrentSession;
        if (session?.SerialConnection == null) return;
        try
        {
            session.SerialConnection.SetDtr(enabled);
            if (session.Host != null) session.Host.SerialDtrEnable = enabled;
            OnPropertyChanged(nameof(IsDtrEnabled));
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to set DTR signal"); }
    }

    public void SetRts(bool enabled)
    {
        var session = _sessionLifecycle.CurrentSession;
        if (session?.SerialConnection == null) return;
        try
        {
            session.SerialConnection.SetRts(enabled);
            if (session.Host != null) session.Host.SerialRtsEnable = enabled;
            OnPropertyChanged(nameof(IsRtsEnabled));
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to set RTS signal"); }
    }

    public void SendBreak()
    {
        try { _sessionLifecycle.CurrentSession?.SerialConnection?.SendBreak(250); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send break signal"); }
    }

    public bool IsLocalEchoEnabled
    {
        get => _sessionLifecycle.SerialBridge?.LocalEcho ?? _sessionLifecycle.CurrentSession?.Host?.SerialLocalEcho ?? false;
        set { if (_sessionLifecycle.SerialBridge != null) { _sessionLifecycle.SerialBridge.LocalEcho = value; OnPropertyChanged(); } }
    }

    public void ToggleLocalEcho()
    {
        if (_sessionLifecycle.SerialBridge != null)
            IsLocalEchoEnabled = !IsLocalEchoEnabled;
    }

    public void NotifySerialStateChanged()
    {
        OnPropertyChanged(nameof(IsSerialConnected));
        OnPropertyChanged(nameof(IsDtrEnabled));
        OnPropertyChanged(nameof(IsRtsEnabled));
        OnPropertyChanged(nameof(IsLocalEchoEnabled));
        (_toggleDtrCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (_toggleRtsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (_sendBreakCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (_toggleLocalEchoCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void LocalEchoCheckBox_Checked(object sender, RoutedEventArgs e) => IsLocalEchoEnabled = true;
    private void LocalEchoCheckBox_Unchecked(object sender, RoutedEventArgs e) => IsLocalEchoEnabled = false;
    private void DtrToggleButton_Checked(object sender, RoutedEventArgs e) => SetDtr(true);
    private void DtrToggleButton_Unchecked(object sender, RoutedEventArgs e) => SetDtr(false);
    private void RtsToggleButton_Checked(object sender, RoutedEventArgs e) => SetRts(true);
    private void RtsToggleButton_Unchecked(object sender, RoutedEventArgs e) => SetRts(false);
    private void SendBreakButton_Click(object sender, RoutedEventArgs e) => SendBreak();

    private void ShowSerialControls()
    {
        SerialControlsPanel.Visibility = Visibility.Visible;
        LocalEchoCheckBox.IsChecked = IsLocalEchoEnabled;
        var host = _sessionLifecycle.CurrentSession?.Host;
        if (host != null)
        {
            DtrToggleButton.IsChecked = host.SerialDtrEnable;
            RtsToggleButton.IsChecked = host.SerialRtsEnable;
        }
        NotifySerialStateChanged();
    }

    private void HideSerialControls() => SerialControlsPanel.Visibility = Visibility.Collapsed;

    #endregion

    #region Status Overlay

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusProgress.Visibility = message.Contains("Connecting") ? Visibility.Visible : Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusOverlay.Visibility = Visibility.Collapsed;

    #endregion

    #region Public Properties

    public string TerminalFontFamily
    {
        get => _themeManager.FontFamily;
        set { _themeManager.FontFamily = value; _themeManager.ApplyFontSettings(TerminalHost); }
    }

    public double TerminalFontSize
    {
        get => _themeManager.FontSize;
        set { _themeManager.FontSize = value; _themeManager.ApplyFontSettings(TerminalHost); }
    }

    public bool IsConnected => _sessionLifecycle.IsConnected;
    public bool IsSerialConnected => _sessionLifecycle.SerialBridge != null &&
        _sessionLifecycle.CurrentSession?.SerialConnection?.IsConnected == true;

    public int ScrollbackBufferSize { get => _outputBuffer.MaxLines; set => _outputBuffer.MaxLines = value; }
    public int MaxLinesInMemory { get => _outputBuffer.MaxLinesInMemory; set => _outputBuffer.MaxLinesInMemory = value; }
    public bool IsPrimaryPane { get => _isPrimaryPane; set => _isPrimaryPane = value; }
    public TerminalTheme? CurrentTheme => _themeManager.CurrentTheme;
    public IBroadcastInputService? BroadcastService { get => _broadcastService; set => _broadcastService = value; }
    public TerminalOutputBuffer OutputBuffer => _outputBuffer;

    public void SetBroadcastService(IBroadcastInputService? service) => _broadcastService = service;
    public void SetServerStatsService(IServerStatsService? service)
    {
        if (_statsCoordinator is TerminalStatsCoordinator coordinator)
        {
            coordinator.SetServerStatsService(service);
        }
    }
    public void SetFocusTracker(ITerminalFocusTracker? tracker) => _focusTracker = tracker;

    public void ApplyTheme(TerminalTheme theme)
    {
        if (theme == null) return;
        _themeManager.ApplyTheme(theme, TerminalHost);
    }

    public void FocusInput() => ((WebTerminalControl)TerminalHost).Focus();

    public void SendCommand(string command)
    {
        if (_sessionLifecycle.SerialBridge != null)
            _sessionLifecycle.SerialBridge.SendCommand(command);
        else
            _sessionLifecycle.SshBridge?.SendCommand(command);
    }

    public string GetOutputText() => _outputBuffer.GetAllText();
    public void RefreshTerminal() => TerminalHost.RefreshVisual();
    public void ClearOutputBuffer() => _outputBuffer.Clear();

    /// <summary>
    /// Disposes the underlying WebView2 terminal control and output buffer.
    /// Call this when the session is permanently closed (not on tab switch).
    /// </summary>
    public void DisposeTerminal()
    {
        _statsCoordinator.Stop();
        _outputBuffer.Dispose();
        _decoderHelper.Dispose();
        ((WebTerminalControl)TerminalHost).Dispose();
    }

    #endregion

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
