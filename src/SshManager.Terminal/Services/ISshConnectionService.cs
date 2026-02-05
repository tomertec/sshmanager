using Renci.SshNet;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Callback for verifying SSH host keys.
/// </summary>
/// <param name="hostname">The hostname being connected to.</param>
/// <param name="port">The port being connected to.</param>
/// <param name="algorithm">The key algorithm (e.g., "ssh-ed25519").</param>
/// <param name="fingerprint">The SHA256 fingerprint in base64 format.</param>
/// <param name="keyBytes">The raw public key bytes.</param>
/// <returns>True to accept the key, false to reject.</returns>
public delegate Task<bool> HostKeyVerificationCallback(
    string hostname,
    int port,
    string algorithm,
    string fingerprint,
    byte[] keyBytes);

/// <summary>
/// Callback for handling keyboard-interactive authentication (2FA/TOTP).
/// </summary>
/// <param name="request">The authentication request containing prompts.</param>
/// <returns>The request with responses filled in, or null to cancel authentication.</returns>
public delegate Task<AuthenticationRequest?> KeyboardInteractiveCallback(AuthenticationRequest request);

/// <summary>
/// Service for establishing SSH connections.
/// </summary>
public interface ISshConnectionService
{
    /// <summary>
    /// Connects to an SSH server and returns a connection with shell stream.
    /// </summary>
    Task<ISshConnection> ConnectAsync(
        TerminalConnectionInfo connectionInfo,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default);

    /// <summary>
    /// Connects to an SSH server with host key verification.
    /// </summary>
    Task<ISshConnection> ConnectAsync(
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default);

    /// <summary>
    /// Connects to an SSH server with host key verification and keyboard-interactive (2FA/TOTP) support.
    /// </summary>
    Task<ISshConnection> ConnectAsync(
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default);

    /// <summary>
    /// Connects through a chain of proxy jump hosts to reach the final target.
    /// Each hop establishes an SSH connection through the previous hop's forwarded port.
    /// </summary>
    /// <param name="connectionChain">
    /// Ordered list of connection info for each hop, ending with the target host.
    /// The first entry is the first jump host (directly reachable).
    /// The last entry is the final target host.
    /// </param>
    /// <param name="hostKeyCallback">Callback for verifying host keys at each hop.</param>
    /// <param name="kbInteractiveCallback">Callback for keyboard-interactive auth at each hop.</param>
    /// <param name="columns">Terminal columns for the final shell.</param>
    /// <param name="rows">Terminal rows for the final shell.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Connection to the final target with an active shell stream.</returns>
    /// <exception cref="ArgumentException">If the connection chain is empty.</exception>
    /// <exception cref="InvalidOperationException">If any hop fails to connect.</exception>
    Task<ISshConnection> ConnectWithProxyChainAsync(
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default);
}

/// <summary>
/// Represents an active SSH connection with shell stream.
/// </summary>
public interface ISshConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The shell stream for terminal I/O.
    /// </summary>
    ShellStream ShellStream { get; }

    /// <summary>
    /// Whether the connection is currently active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Resizes the terminal window on the remote host.
    /// </summary>
    /// <param name="columns">The new terminal width in columns.</param>
    /// <param name="rows">The new terminal height in rows.</param>
    /// <returns>True if the resize was successful, false otherwise.</returns>
    bool ResizeTerminal(uint columns, uint rows);

    /// <summary>
    /// Runs a command on the server and returns the output.
    /// This uses a separate channel and does not interfere with the shell stream.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="timeout">Timeout for command execution.</param>
    /// <returns>The command output, or null if execution failed.</returns>
    Task<string?> RunCommandAsync(string command, TimeSpan? timeout = null);

    /// <summary>
    /// Sends a keep-alive packet to verify the connection is still active.
    /// This performs an active check by sending an SSH_MSG_IGNORE packet.
    /// </summary>
    /// <returns>True if keep-alive was sent successfully, false if the connection is dead.</returns>
    bool TrySendKeepAlive();
}
