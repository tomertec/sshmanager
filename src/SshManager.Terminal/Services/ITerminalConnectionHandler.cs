using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handler interface for terminal connection operations.
/// Manages SSH connection establishment and teardown.
/// </summary>
public interface ITerminalConnectionHandler
{
    /// <summary>
    /// Establishes a direct SSH connection.
    /// </summary>
    /// <param name="sshService">The SSH connection service.</param>
    /// <param name="connectionInfo">Connection parameters.</param>
    /// <param name="hostKeyCallback">Optional callback for host key verification.</param>
    /// <param name="kbInteractiveCallback">Optional callback for keyboard-interactive auth.</param>
    /// <param name="columns">Terminal width in columns.</param>
    /// <param name="rows">Terminal height in rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The connection result containing the SSH connection and bridge.</returns>
    Task<TerminalConnectionResult> ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken ct);

    /// <summary>
    /// Establishes an SSH connection through a proxy chain.
    /// </summary>
    /// <param name="sshService">The SSH connection service.</param>
    /// <param name="connectionChain">The proxy chain, ending with the target host.</param>
    /// <param name="hostKeyCallback">Optional callback for host key verification at each hop.</param>
    /// <param name="kbInteractiveCallback">Optional callback for keyboard-interactive auth at each hop.</param>
    /// <param name="columns">Terminal width in columns.</param>
    /// <param name="rows">Terminal height in rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The connection result containing the SSH connection and bridge.</returns>
    Task<TerminalConnectionResult> ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken ct);

    /// <summary>
    /// Creates a bridge for attaching to an existing session.
    /// </summary>
    /// <param name="session">The session to attach to.</param>
    /// <returns>The bridge to use, either existing or newly created.</returns>
    TerminalAttachResult AttachToSession(TerminalSession session);

    /// <summary>
    /// Disconnects and cleans up the specified bridge.
    /// </summary>
    /// <param name="bridge">The bridge to disconnect.</param>
    /// <param name="ownsBridge">Whether this caller owns the bridge (should dispose it).</param>
    void Disconnect(SshTerminalBridge? bridge, bool ownsBridge);
}

/// <summary>
/// Result of establishing a terminal connection.
/// </summary>
/// <param name="Connection">The established SSH connection.</param>
/// <param name="Bridge">The SSH terminal bridge for data flow.</param>
public record TerminalConnectionResult(
    ISshConnection Connection,
    SshTerminalBridge Bridge);

/// <summary>
/// Result of attaching to an existing session.
/// </summary>
/// <param name="Bridge">The bridge to use for terminal I/O.</param>
/// <param name="OwnsBridge">Whether the caller owns the bridge and should dispose it.</param>
/// <param name="NeedsStartReading">Whether StartReading needs to be called on the bridge.</param>
public record TerminalAttachResult(
    SshTerminalBridge? Bridge,
    bool OwnsBridge,
    bool NeedsStartReading);
