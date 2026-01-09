using SshManager.Terminal;

namespace SshManager.App.Services;

/// <summary>
/// Service interface for orchestrating terminal session connections.
/// Handles both SSH and serial port connections, manages connection lifecycle,
/// and coordinates with related services (proxy jump, port forwarding, etc.).
/// </summary>
public interface ISessionConnectionService
{
    /// <summary>
    /// Event raised when a connection attempt completes (success or failure).
    /// Subscribers can use this to track connection results and update UI.
    /// </summary>
    event EventHandler<SessionConnectionResultEventArgs>? ConnectionCompleted;

    /// <summary>
    /// Connects an SSH session to a terminal pane target.
    /// Handles both direct connections and proxy jump chains.
    /// </summary>
    /// <param name="paneTarget">The terminal pane to connect to.</param>
    /// <param name="session">The terminal session to establish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the connection is established or fails.</returns>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item>Checks if the host has a ProxyJump profile and resolves the connection chain</item>
    /// <item>Creates appropriate host key verification and keyboard-interactive callbacks</item>
    /// <item>Establishes the SSH connection (direct or via proxy chain)</item>
    /// <item>Records connection history</item>
    /// <item>Raises ConnectionCompleted event with the result</item>
    /// </list>
    /// </remarks>
    Task ConnectSshSessionAsync(
        ITerminalPaneTarget paneTarget,
        TerminalSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects a serial port session to a terminal pane target.
    /// </summary>
    /// <param name="paneTarget">The terminal pane to connect to.</param>
    /// <param name="session">The terminal session to establish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the connection is established or fails.</returns>
    /// <remarks>
    /// This method:
    /// <list type="bullet">
    /// <item>Creates serial connection info from the host entry</item>
    /// <item>Establishes the serial connection using the serial service</item>
    /// <item>Records connection history</item>
    /// <item>Raises ConnectionCompleted event with the result</item>
    /// </list>
    /// </remarks>
    Task ConnectSerialSessionAsync(
        ITerminalPaneTarget paneTarget,
        TerminalSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts auto-start port forwardings for a successfully connected SSH session.
    /// </summary>
    /// <param name="session">The connected SSH session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when auto-start forwardings are started.</returns>
    /// <remarks>
    /// This method should be called after a successful SSH connection is established.
    /// It will:
    /// <list type="bullet">
    /// <item>Query the port forwarding service for auto-start profiles</item>
    /// <item>Start each auto-start forwarding</item>
    /// <item>Log the results via the session logger</item>
    /// </list>
    /// Errors during port forwarding setup are logged but do not fail the connection.
    /// </remarks>
    Task StartAutoStartPortForwardingsAsync(
        TerminalSession session,
        CancellationToken cancellationToken = default);
}
