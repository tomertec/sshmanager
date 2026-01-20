using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services.Connection;

/// <summary>
/// Implementation of <see cref="ISerialSessionConnector"/> that manages serial port connection lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates serial connection logic extracted from SshTerminalControl.
/// It manages bridge creation and event wiring without holding references to UI elements.
/// </para>
/// <para>
/// <b>Data Flow:</b>
/// <code>
/// Serial Port --> SerialTerminalBridge --> DataReceived event --> Consumer (UI control)
/// </code>
/// </para>
/// </remarks>
public sealed class SerialSessionConnector : ISerialSessionConnector
{
    private readonly ILogger<SerialSessionConnector> _logger;

    // Track active bridges to manage event subscriptions
    private readonly Dictionary<SerialTerminalBridge, BridgeEventHandlers> _bridgeHandlers = new();
    private readonly object _handlersLock = new();

    // Track whether disconnection has been raised to prevent duplicate events
    private volatile bool _disconnectedRaised;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <summary>
    /// Creates a new SerialSessionConnector instance.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public SerialSessionConnector(ILogger<SerialSessionConnector>? logger = null)
    {
        _logger = logger ?? NullLogger<SerialSessionConnector>.Instance;
    }

    /// <inheritdoc />
    public async Task<SerialConnectionResult> ConnectAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serialService);
        ArgumentNullException.ThrowIfNull(connectionInfo);

        // Reset disconnect tracking for new connection
        _disconnectedRaised = false;

        _logger.LogDebug("Connecting to serial port {PortName} at {BaudRate} baud",
            connectionInfo.PortName, connectionInfo.BaudRate);

        // Connect to serial port
        var connection = await serialService.ConnectAsync(connectionInfo, cancellationToken);

        // Create bridge with local echo and line ending settings from connection info
        var bridge = new SerialTerminalBridge(
            connection.BaseStream,
            logger: null,
            localEcho: connectionInfo.LocalEcho,
            lineEnding: connectionInfo.LineEnding);

        _logger.LogInformation("Connected to serial port {PortName}", connectionInfo.PortName);

        return new SerialConnectionResult(connection, bridge);
    }

    /// <inheritdoc />
    public void WireBridgeEvents(SerialTerminalBridge bridge, Action<byte[]> onDataReceived)
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
            Action disconnectedHandler = () => OnBridgeDisconnected(bridge);

            // Subscribe to bridge events
            bridge.DataReceived += dataHandler;
            bridge.Disconnected += disconnectedHandler;

            // Track handlers for later removal
            _bridgeHandlers[bridge] = new BridgeEventHandlers(dataHandler, disconnectedHandler);

            _logger.LogDebug("Wired events for serial bridge");
        }
    }

    /// <inheritdoc />
    public void UnwireBridgeEvents(SerialTerminalBridge bridge)
    {
        if (bridge == null) return;

        lock (_handlersLock)
        {
            UnwireBridgeEventsInternal(bridge);
        }
    }

    /// <inheritdoc />
    public void Disconnect(SerialTerminalBridge bridge, bool ownsBridge, ISerialConnection? connection)
    {
        if (bridge == null) return;

        _logger.LogDebug("Disconnecting serial session");

        // Unwire events first
        UnwireBridgeEvents(bridge);

        // Dispose if owned
        if (ownsBridge)
        {
            try
            {
                bridge.Dispose();
                _logger.LogDebug("Disposed serial terminal bridge");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing serial terminal bridge");
            }
        }

        // Dispose connection if provided
        if (connection != null)
        {
            try
            {
                connection.Dispose();
                _logger.LogDebug("Disposed serial connection");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing serial connection");
            }
        }
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

    private void UnwireBridgeEventsInternal(SerialTerminalBridge bridge)
    {
        if (_bridgeHandlers.TryGetValue(bridge, out var handlers))
        {
            bridge.DataReceived -= handlers.DataHandler;
            bridge.Disconnected -= handlers.DisconnectedHandler;
            _bridgeHandlers.Remove(bridge);
            _logger.LogDebug("Unwired events for serial bridge");
        }
    }

    private void OnBridgeDisconnected(SerialTerminalBridge bridge)
    {
        // Only raise disconnected event once per connection
        if (_disconnectedRaised) return;
        _disconnectedRaised = true;

        _logger.LogInformation("Serial bridge disconnected");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Holds references to event handlers for a specific bridge, allowing proper cleanup.
    /// </summary>
    private sealed record BridgeEventHandlers(
        Action<byte[]> DataHandler,
        Action DisconnectedHandler);
}
