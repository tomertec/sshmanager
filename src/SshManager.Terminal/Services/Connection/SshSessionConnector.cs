using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services.Connection;

/// <summary>
/// Service for establishing SSH terminal sessions.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates SSH connection establishment logic extracted from
/// SshTerminalControl, providing a clean separation between UI and connection logic.
/// </para>
/// <para>
/// <b>Architecture:</b> This service uses ITerminalConnectionHandler internally for
/// the low-level connection establishment, adding session-level event management
/// and bridge lifecycle coordination on top.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Public methods can be called from any thread. Event handlers
/// are invoked on the thread where the SSH data is received (typically a background thread).
/// Callers are responsible for dispatching to the UI thread if needed.
/// </para>
/// </remarks>
public sealed class SshSessionConnector : ISshSessionConnector
{
    private readonly ITerminalConnectionHandler _connectionHandler;
    private readonly ILogger<SshSessionConnector> _logger;

    // Track active bridges to manage event subscriptions
    private readonly Dictionary<SshTerminalBridge, BridgeEventHandlers> _bridgeHandlers = new();
    private readonly object _handlersLock = new();

    // Track whether disconnection has been raised to prevent duplicate events
    private volatile bool _disconnectedRaised;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <summary>
    /// Initializes a new instance of the SshSessionConnector class.
    /// </summary>
    /// <param name="connectionHandler">The underlying connection handler for establishing connections.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public SshSessionConnector(
        ITerminalConnectionHandler? connectionHandler = null,
        ILogger<SshSessionConnector>? logger = null)
    {
        _connectionHandler = connectionHandler ?? new TerminalConnectionHandler();
        _logger = logger ?? NullLogger<SshSessionConnector>.Instance;
    }

    /// <inheritdoc />
    public async Task<SshConnectionResult> ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sshService);
        ArgumentNullException.ThrowIfNull(connectionInfo);

        // Reset disconnect tracking for new connection
        _disconnectedRaised = false;

        _logger.LogInformation("Establishing SSH connection to {Host}:{Port}",
            connectionInfo.Hostname, connectionInfo.Port);

        try
        {
            // Delegate to connection handler for actual connection establishment
            var result = await _connectionHandler.ConnectAsync(
                sshService,
                connectionInfo,
                hostKeyCallback,
                kbInteractiveCallback,
                columns,
                rows,
                cancellationToken);

            _logger.LogInformation("SSH connection established to {Host}", connectionInfo.Hostname);

            return new SshConnectionResult(result.Connection, result.Bridge);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish SSH connection to {Host}:{Port}",
                connectionInfo.Hostname, connectionInfo.Port);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SshConnectionResult> ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sshService);
        if (connectionChain == null || connectionChain.Count == 0)
        {
            throw new ArgumentException("Connection chain cannot be empty", nameof(connectionChain));
        }

        // Reset disconnect tracking for new connection
        _disconnectedRaised = false;

        var targetHost = connectionChain[^1];
        var chainDescription = string.Join(" -> ", connectionChain.Select(c => c.Hostname));
        _logger.LogInformation("Establishing SSH connection through proxy chain: {Chain}", chainDescription);

        try
        {
            // Delegate to connection handler for proxy chain connection
            var result = await _connectionHandler.ConnectWithProxyChainAsync(
                sshService,
                connectionChain,
                hostKeyCallback,
                kbInteractiveCallback,
                columns,
                rows,
                cancellationToken);

            _logger.LogInformation("SSH connection established through proxy chain to {Target}", targetHost.Hostname);

            return new SshConnectionResult(result.Connection, result.Bridge);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish SSH connection through proxy chain: {Chain}", chainDescription);
            throw;
        }
    }

    /// <inheritdoc />
    public void WireBridgeEvents(SshTerminalBridge bridge, Action<byte[]> onDataReceived)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(onDataReceived);

        lock (_handlersLock)
        {
            // Unwire existing handlers if any (prevent duplicate subscriptions)
            if (_bridgeHandlers.ContainsKey(bridge))
            {
                UnwireBridgeEventsInternal(bridge);
            }

            // Create typed event handlers we can unsubscribe later
            Action<byte[]> dataHandler = onDataReceived;
            EventHandler disconnectedHandler = OnBridgeDisconnected;

            // Subscribe to bridge events
            bridge.DataReceived += dataHandler;
            bridge.Disconnected += disconnectedHandler;

            // Track handlers for later removal
            _bridgeHandlers[bridge] = new BridgeEventHandlers(dataHandler, disconnectedHandler);

            _logger.LogDebug("Wired events for SSH bridge");
        }
    }

    /// <inheritdoc />
    public void UnwireBridgeEvents(SshTerminalBridge bridge)
    {
        if (bridge == null) return;

        lock (_handlersLock)
        {
            UnwireBridgeEventsInternal(bridge);
        }
    }

    /// <inheritdoc />
    public void Disconnect(SshTerminalBridge bridge, bool ownsBridge)
    {
        if (bridge == null) return;

        // Unwire events first
        UnwireBridgeEvents(bridge);

        // Dispose if owned
        if (ownsBridge)
        {
            try
            {
                bridge.Dispose();
                _logger.LogDebug("Disposed SSH terminal bridge");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SSH terminal bridge");
            }
        }
    }

    /// <summary>
    /// Wires up event handlers for connection-level disconnection monitoring.
    /// </summary>
    /// <param name="connection">The SSH connection to monitor.</param>
    /// <remarks>
    /// Call this after establishing a connection to get early notification when the
    /// connection drops (e.g., server shutdown). This is faster than waiting for the
    /// bridge read loop to detect the disconnection.
    /// </remarks>
    public void WireConnectionEvents(ISshConnection connection)
    {
        if (connection == null) return;

        connection.Disconnected += OnConnectionDisconnected;
        _logger.LogDebug("Wired connection disconnection event");
    }

    /// <summary>
    /// Removes event handlers from connection-level disconnection monitoring.
    /// </summary>
    /// <param name="connection">The SSH connection to stop monitoring.</param>
    public void UnwireConnectionEvents(ISshConnection connection)
    {
        if (connection == null) return;

        connection.Disconnected -= OnConnectionDisconnected;
        _logger.LogDebug("Unwired connection disconnection event");
    }

    /// <summary>
    /// Resets the disconnect tracking state.
    /// </summary>
    /// <remarks>
    /// Call this when reusing the connector for a new connection to ensure
    /// the Disconnected event can fire again.
    /// </remarks>
    public void ResetDisconnectTracking()
    {
        _disconnectedRaised = false;
    }

    private void UnwireBridgeEventsInternal(SshTerminalBridge bridge)
    {
        if (_bridgeHandlers.TryGetValue(bridge, out var handlers))
        {
            bridge.DataReceived -= handlers.DataHandler;
            bridge.Disconnected -= handlers.DisconnectedHandler;
            _bridgeHandlers.Remove(bridge);
            _logger.LogDebug("Unwired events for SSH bridge");
        }
    }

    private void OnBridgeDisconnected(object? sender, EventArgs e)
    {
        RaiseDisconnectedOnce("bridge read loop ended");
    }

    private void OnConnectionDisconnected(object? sender, EventArgs e)
    {
        RaiseDisconnectedOnce("connection-level disconnect");
    }

    private void RaiseDisconnectedOnce(string reason)
    {
        // Only raise disconnected event once per connection
        if (_disconnectedRaised) return;
        _disconnectedRaised = true;

        _logger.LogInformation("SSH connection disconnected ({Reason})", reason);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Holds references to event handlers for a specific bridge, allowing proper cleanup.
    /// </summary>
    private sealed record BridgeEventHandlers(
        Action<byte[]> DataHandler,
        EventHandler DisconnectedHandler);
}
