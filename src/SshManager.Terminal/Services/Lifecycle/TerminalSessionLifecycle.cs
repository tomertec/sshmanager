using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Services.Connection;

namespace SshManager.Terminal.Services.Lifecycle;

/// <summary>
/// Implementation of <see cref="ITerminalSessionLifecycle"/> that manages terminal session lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates session lifecycle management logic extracted from SshTerminalControl,
/// including session attachment for mirroring, detachment, and full disconnection with cleanup.
/// </para>
/// <para>
/// <b>Ownership Model:</b>
/// <list type="bullet">
/// <item>When a session is created via ConnectAsync: OwnsBridge = true, dispose on disconnect</item>
/// <item>When attached to existing session (mirroring): OwnsBridge = false, do not dispose</item>
/// </list>
/// </para>
/// <para>
/// <b>Thread Safety:</b> Public methods should be called from the UI thread.
/// Events may be raised on background threads (from bridge disconnection detection).
/// </para>
/// </remarks>
public sealed class TerminalSessionLifecycle : ITerminalSessionLifecycle
{
    private readonly ILogger<TerminalSessionLifecycle> _logger;

    private TerminalSession? _session;
    private SshTerminalBridge? _sshBridge;
    private SerialTerminalBridge? _serialBridge;
    private bool _ownsBridge;
    private Action<byte[]>? _sshDataReceivedCallback;
    private Action? _disconnectedCallback;

    /// <summary>
    /// Creates a new TerminalSessionLifecycle instance.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public TerminalSessionLifecycle(ILogger<TerminalSessionLifecycle>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalSessionLifecycle>.Instance;
    }

    /// <inheritdoc />
    public TerminalSession? CurrentSession => _session;

    /// <inheritdoc />
    public bool OwnsBridge => _ownsBridge;

    /// <inheritdoc />
    public bool IsConnected =>
        _session?.Connection?.IsConnected == true ||
        _session?.SerialConnection?.IsConnected == true;

    /// <inheritdoc />
    public SshTerminalBridge? SshBridge => _sshBridge;

    /// <inheritdoc />
    public SerialTerminalBridge? SerialBridge => _serialBridge;

    /// <inheritdoc />
    public event EventHandler? SessionAttached;

    /// <inheritdoc />
    public event EventHandler? SessionDetached;

    /// <inheritdoc />
    public async Task AttachToSessionAsync(
        TerminalSession session,
        WebTerminalControl terminal,
        ITerminalConnectionHandler connectionHandler)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(connectionHandler);

        // Use default data handler that writes to terminal
        await AttachToSessionAsync(
            session,
            terminal,
            connectionHandler,
            onSshDataReceived: null);
    }

    /// <inheritdoc />
    public async Task AttachToSessionAsync(
        TerminalSession session,
        WebTerminalControl terminal,
        ITerminalConnectionHandler connectionHandler,
        Action<byte[]>? onSshDataReceived)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(connectionHandler);

        _session = session;
        _sshDataReceivedCallback = onSshDataReceived;

        _logger.LogDebug("Attaching to session: {Title}, IsConnected: {IsConnected}",
            session.Title, session.IsConnected);

        if (session.IsConnected)
        {
            // Initialize WebTerminalControl first
            await terminal.InitializeAsync();

            // Use handler to attach to session
            var attachResult = connectionHandler.AttachToSession(session);

            if (attachResult.Bridge != null)
            {
                _sshBridge = attachResult.Bridge;
                _ownsBridge = attachResult.OwnsBridge;

                // Wire data received event if callback provided
                if (_sshDataReceivedCallback != null)
                {
                    _sshBridge.DataReceived += OnSshDataReceived;
                }

                if (attachResult.OwnsBridge)
                {
                    // Subscribe to disconnection events if we own the bridge
                    _sshBridge.Disconnected += OnBridgeDisconnected;

                    // Subscribe to connection-level disconnection for faster detection
                    if (session.Connection != null)
                    {
                        session.Connection.Disconnected += OnConnectionDisconnected;
                    }
                }

                if (attachResult.NeedsStartReading)
                {
                    _sshBridge.StartReading();
                }

                _logger.LogDebug("Attached to session: {Title}, OwnsBridge: {OwnsBridge}",
                    session.Title, _ownsBridge);

                SessionAttached?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _logger.LogWarning("Failed to get bridge for session: {Title}", session.Title);
            }
        }
        else
        {
            _logger.LogDebug("Session {Title} is not connected, skipping attachment", session.Title);
        }
    }

    /// <inheritdoc />
    public void SetSession(TerminalSession? session, SshTerminalBridge? sshBridge, bool ownsBridge)
    {
        _session = session;
        _sshBridge = sshBridge;
        _ownsBridge = ownsBridge;
        _serialBridge = null;

        _logger.LogDebug("Session set: {Title}, OwnsBridge: {OwnsBridge}",
            session?.Title ?? "(null)", ownsBridge);
    }

    /// <inheritdoc />
    public void SetSerialSession(TerminalSession? session, SerialTerminalBridge? serialBridge, bool ownsBridge)
    {
        _session = session;
        _serialBridge = serialBridge;
        _ownsBridge = ownsBridge;
        _sshBridge = null;

        _logger.LogDebug("Serial session set: {Title}, OwnsBridge: {OwnsBridge}",
            session?.Title ?? "(null)", ownsBridge);
    }

    /// <inheritdoc />
    public void Detach()
    {
        _logger.LogDebug("Detaching from session: {Title}", _session?.Title);

        // Unwire SSH bridge events
        if (_sshBridge != null)
        {
            if (_sshDataReceivedCallback != null)
            {
                _sshBridge.DataReceived -= OnSshDataReceived;
            }

            if (_ownsBridge)
            {
                _sshBridge.Disconnected -= OnBridgeDisconnected;

                // Dispose bridge if we own it
                _sshBridge.Dispose();
            }

            _sshBridge = null;
        }

        // Unwire connection events
        if (_ownsBridge && _session?.Connection != null)
        {
            _session.Connection.Disconnected -= OnConnectionDisconnected;
        }

        // Clear state
        _session = null;
        _ownsBridge = false;
        _sshDataReceivedCallback = null;
        _disconnectedCallback = null;
        _serialBridge = null;

        _logger.LogDebug("Detached from session");
        SessionDetached?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Disconnect(
        ISshSessionConnector? sshConnector,
        ISerialSessionConnector? serialConnector)
    {
        _logger.LogDebug("Disconnecting session: {Title}", _session?.Title);

        // Unwire and cleanup SSH bridge
        if (_sshBridge != null)
        {
            // Unwire through connector if available
            sshConnector?.UnwireBridgeEvents(_sshBridge);

            // Also unwire our local event handlers
            if (_sshDataReceivedCallback != null)
            {
                _sshBridge.DataReceived -= OnSshDataReceived;
            }

            if (_ownsBridge)
            {
                _sshBridge.Disconnected -= OnBridgeDisconnected;
                _sshBridge.Dispose();
                _logger.LogDebug("SSH bridge disposed");
            }

            _sshBridge = null;
        }

        // Unwire and cleanup serial bridge
        if (_serialBridge != null)
        {
            // Unwire through connector if available
            serialConnector?.UnwireBridgeEvents(_serialBridge);

            if (_ownsBridge)
            {
                serialConnector?.Disconnect(_serialBridge, _ownsBridge, _session?.SerialConnection);
                _logger.LogDebug("Serial bridge disconnected");
            }

            _serialBridge = null;
        }

        // Cleanup SSH connection
        if (_session?.Connection != null)
        {
            // Unwire connection events through connector
            if (sshConnector is SshSessionConnector concreteConnector)
            {
                concreteConnector.UnwireConnectionEvents(_session.Connection);
            }

            // Also unwire our local handler
            _session.Connection.Disconnected -= OnConnectionDisconnected;

            if (_ownsBridge)
            {
                _session.Connection.Dispose();
                _logger.LogDebug("SSH connection disposed");
            }
        }

        // Clear state
        var sessionTitle = _session?.Title;
        _session = null;
        _ownsBridge = false;
        _sshDataReceivedCallback = null;
        _disconnectedCallback = null;

        _logger.LogInformation("Session {Title} disconnected", sessionTitle);
        SessionDetached?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        _logger.LogDebug("Disconnecting session (state clear only): {Title}", _session?.Title);

        // Just clear the state - caller is responsible for disposing bridges/connections
        var sessionTitle = _session?.Title;
        _session = null;
        _sshBridge = null;
        _serialBridge = null;
        _ownsBridge = false;
        _sshDataReceivedCallback = null;
        _disconnectedCallback = null;

        _logger.LogDebug("Session {Title} state cleared", sessionTitle);
        SessionDetached?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void WireDisconnectionHandlers(
        ISshSessionConnector sshConnector,
        Action onDisconnected)
    {
        ArgumentNullException.ThrowIfNull(sshConnector);
        ArgumentNullException.ThrowIfNull(onDisconnected);

        _disconnectedCallback = onDisconnected;

        // Wire connection events if we have a session with connection
        if (_session?.Connection != null && sshConnector is SshSessionConnector concreteConnector)
        {
            concreteConnector.WireConnectionEvents(_session.Connection);
        }

        _logger.LogDebug("Disconnection handlers wired");
    }

    /// <inheritdoc />
    public void UnwireDisconnectionHandlers(ISshSessionConnector sshConnector)
    {
        if (sshConnector == null) return;

        // Unwire connection events
        if (_session?.Connection != null && sshConnector is SshSessionConnector concreteConnector)
        {
            concreteConnector.UnwireConnectionEvents(_session.Connection);
        }

        _disconnectedCallback = null;
        _logger.LogDebug("Disconnection handlers unwired");
    }

    /// <summary>
    /// Handles SSH data received from the bridge.
    /// </summary>
    private void OnSshDataReceived(byte[] data)
    {
        try
        {
            _sshDataReceivedCallback?.Invoke(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSH data received callback");
        }
    }

    /// <summary>
    /// Handles bridge disconnection.
    /// </summary>
    private void OnBridgeDisconnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Bridge disconnected for session: {Title}", _session?.Title);

        try
        {
            _disconnectedCallback?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in disconnection callback");
        }
    }

    /// <summary>
    /// Handles connection-level disconnection (faster detection than bridge).
    /// </summary>
    private void OnConnectionDisconnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Connection disconnected for session: {Title}", _session?.Title);

        try
        {
            _disconnectedCallback?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in disconnection callback");
        }
    }
}
