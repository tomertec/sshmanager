using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services.Connection;

/// <summary>
/// Service interface for establishing SSH terminal sessions.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates SSH connection logic extracted from SshTerminalControl,
/// handling both direct connections and proxy chain (jump host) connections.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This service may be called from the UI thread. Async methods
/// return completed Tasks that may be awaited. Event wiring/unwiring should be done
/// from the UI thread to avoid race conditions.
/// </para>
/// </remarks>
public interface ISshSessionConnector
{
    /// <summary>
    /// Establishes a direct SSH connection using the provided connection parameters.
    /// </summary>
    /// <param name="sshService">The SSH connection service to use.</param>
    /// <param name="connectionInfo">Connection parameters including hostname, port, auth method.</param>
    /// <param name="hostKeyCallback">Optional callback for host key verification.</param>
    /// <param name="kbInteractiveCallback">Optional callback for keyboard-interactive (2FA/TOTP) auth.</param>
    /// <param name="columns">Terminal width in columns.</param>
    /// <param name="rows">Terminal height in rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the established connection and bridge for terminal I/O.</returns>
    Task<SshConnectionResult> ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Establishes an SSH connection through a proxy chain (jump hosts).
    /// </summary>
    /// <param name="sshService">The SSH connection service to use.</param>
    /// <param name="connectionChain">Ordered list of hosts, ending with the target. First entry is directly reachable.</param>
    /// <param name="hostKeyCallback">Optional callback for host key verification at each hop.</param>
    /// <param name="kbInteractiveCallback">Optional callback for keyboard-interactive auth at each hop.</param>
    /// <param name="columns">Terminal width in columns.</param>
    /// <param name="rows">Terminal height in rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the established connection and bridge for terminal I/O.</returns>
    Task<SshConnectionResult> ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wires up event handlers for an SSH terminal bridge.
    /// </summary>
    /// <param name="bridge">The bridge to wire events for.</param>
    /// <param name="onDataReceived">Callback invoked when data is received from SSH server.</param>
    /// <remarks>
    /// Call this after establishing a connection to start receiving data.
    /// The bridge's DataReceived event will invoke the provided callback.
    /// </remarks>
    void WireBridgeEvents(SshTerminalBridge bridge, Action<byte[]> onDataReceived);

    /// <summary>
    /// Removes event handlers from an SSH terminal bridge.
    /// </summary>
    /// <param name="bridge">The bridge to unwire events from.</param>
    /// <remarks>
    /// Call this before disposing a bridge or when detaching from a session.
    /// </remarks>
    void UnwireBridgeEvents(SshTerminalBridge bridge);

    /// <summary>
    /// Disconnects and cleans up an SSH terminal bridge.
    /// </summary>
    /// <param name="bridge">The bridge to disconnect.</param>
    /// <param name="ownsBridge">Whether this control owns the bridge (and should dispose it).</param>
    /// <remarks>
    /// Unwires events and disposes the bridge if owned. Does not dispose the SSH connection.
    /// </remarks>
    void Disconnect(SshTerminalBridge bridge, bool ownsBridge);

    /// <summary>
    /// Event raised when an SSH connection is disconnected.
    /// </summary>
    /// <remarks>
    /// This event aggregates disconnection signals from both the bridge (read loop ended)
    /// and the connection (SSH.NET reported disconnect). It fires at most once per connection.
    /// </remarks>
    event EventHandler? Disconnected;
}

/// <summary>
/// Result of establishing an SSH session connection.
/// </summary>
/// <param name="Connection">The established SSH connection providing shell stream access.</param>
/// <param name="Bridge">The SSH terminal bridge for bidirectional data flow.</param>
public record SshConnectionResult(
    ISshConnection Connection,
    SshTerminalBridge Bridge);
