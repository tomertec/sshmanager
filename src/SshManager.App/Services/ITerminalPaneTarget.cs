using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.Services;

/// <summary>
/// Abstraction for a terminal pane that can establish SSH or serial connections.
/// Provides a unified interface for connecting panes to different connection types.
/// </summary>
public interface ITerminalPaneTarget
{
    /// <summary>
    /// Gets the underlying SSH terminal control for this pane.
    /// </summary>
    Terminal.Controls.SshTerminalControl TerminalControl { get; }

    /// <summary>
    /// Connects the terminal pane to an SSH session using direct connection.
    /// </summary>
    /// <param name="sshService">The SSH connection service.</param>
    /// <param name="connectionInfo">SSH connection parameters.</param>
    /// <param name="hostKeyCallback">Callback for verifying host keys.</param>
    /// <param name="kbInteractiveCallback">Callback for keyboard-interactive authentication.</param>
    /// <returns>A task that completes when the connection is established.</returns>
    Task ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback);

    /// <summary>
    /// Connects the terminal pane to an SSH session through a proxy jump chain.
    /// </summary>
    /// <param name="sshService">The SSH connection service.</param>
    /// <param name="connectionChain">Ordered list of connection info for each hop, ending with the target host.</param>
    /// <param name="hostKeyCallback">Callback for verifying host keys at each hop.</param>
    /// <param name="kbInteractiveCallback">Callback for keyboard-interactive auth at each hop.</param>
    /// <returns>A task that completes when the connection chain is established.</returns>
    Task ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback);

    /// <summary>
    /// Connects the terminal pane to a serial port session.
    /// </summary>
    /// <param name="serialService">The serial connection service.</param>
    /// <param name="connectionInfo">Serial port connection parameters.</param>
    /// <param name="session">The terminal session to associate with the connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the serial connection is established.</returns>
    Task ConnectSerialAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        TerminalSession session,
        CancellationToken cancellationToken = default);
}
