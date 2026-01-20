using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services.Connection;

/// <summary>
/// Service for managing serial port connection lifecycle.
/// Handles establishing connections, bridge wiring, and reconnection for serial sessions.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates the serial connection logic previously embedded in SshTerminalControl.
/// It manages the connection lifecycle without holding references to UI elements.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Methods should be called from the UI thread. The service
/// uses events to notify callers of data received and disconnection, which may fire
/// on background threads.
/// </para>
/// </remarks>
public interface ISerialSessionConnector
{
    /// <summary>
    /// Connects to a serial port using the provided service and connection info.
    /// </summary>
    /// <param name="serialService">The serial connection service.</param>
    /// <param name="connectionInfo">Serial port connection parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the established connection and bridge for terminal I/O.</returns>
    /// <exception cref="ArgumentNullException">If any required parameter is null.</exception>
    /// <exception cref="InvalidOperationException">If the port cannot be opened.</exception>
    Task<SerialConnectionResult> ConnectAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wires up events from the serial bridge to receive data and handle disconnection.
    /// </summary>
    /// <param name="bridge">The serial terminal bridge to wire.</param>
    /// <param name="onDataReceived">Callback invoked when data is received from the serial port.</param>
    void WireBridgeEvents(SerialTerminalBridge bridge, Action<byte[]> onDataReceived);

    /// <summary>
    /// Removes event handlers from the serial bridge.
    /// </summary>
    /// <param name="bridge">The serial terminal bridge to unwire.</param>
    void UnwireBridgeEvents(SerialTerminalBridge bridge);

    /// <summary>
    /// Disconnects and cleans up a serial terminal bridge.
    /// </summary>
    /// <param name="bridge">The bridge to disconnect.</param>
    /// <param name="ownsBridge">Whether this control owns the bridge (and should dispose it).</param>
    /// <param name="connection">The serial connection to dispose, if any.</param>
    void Disconnect(SerialTerminalBridge bridge, bool ownsBridge, ISerialConnection? connection);

    /// <summary>
    /// Event raised when the serial connection is disconnected.
    /// </summary>
    event EventHandler? Disconnected;
}

/// <summary>
/// Result of establishing a serial session connection.
/// </summary>
/// <param name="Connection">The established serial connection.</param>
/// <param name="Bridge">The serial terminal bridge for bidirectional data flow.</param>
public record SerialConnectionResult(
    ISerialConnection Connection,
    SerialTerminalBridge Bridge);
