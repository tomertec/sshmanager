using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for managing SSH port forwarding.
/// </summary>
public interface IPortForwardingService
{
    /// <summary>
    /// Event raised when a forwarding status changes.
    /// </summary>
    event EventHandler<PortForwardingStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Starts a port forwarding on an active SSH connection.
    /// </summary>
    /// <param name="connection">The SSH connection to use for forwarding.</param>
    /// <param name="sessionId">The session ID to associate with this forwarding.</param>
    /// <param name="profile">The profile defining the forwarding configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A handle to manage the forwarding, or null if starting failed.</returns>
    Task<PortForwardingHandle?> StartForwardingAsync(
        ISshConnection connection,
        Guid sessionId,
        PortForwardingProfile profile,
        CancellationToken ct = default);

    /// <summary>
    /// Stops a specific port forwarding.
    /// </summary>
    /// <param name="handle">The handle of the forwarding to stop.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopForwardingAsync(PortForwardingHandle handle, CancellationToken ct = default);

    /// <summary>
    /// Stops a forwarding by its ID.
    /// </summary>
    /// <param name="forwardingId">The ID of the forwarding to stop.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopForwardingAsync(Guid forwardingId, CancellationToken ct = default);

    /// <summary>
    /// Gets all active port forwardings.
    /// </summary>
    IReadOnlyList<ActivePortForwarding> GetActiveForwardings();

    /// <summary>
    /// Gets active port forwardings for a specific session.
    /// </summary>
    /// <param name="sessionId">The session ID to filter by.</param>
    IReadOnlyList<ActivePortForwarding> GetActiveForwardings(Guid sessionId);

    /// <summary>
    /// Stops all port forwardings for a session.
    /// </summary>
    /// <param name="sessionId">The session ID to stop forwardings for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopAllForSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Stops all active port forwardings.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StopAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a local port is currently in use by an active forwarding.
    /// </summary>
    /// <param name="localPort">The port to check.</param>
    /// <returns>True if the port is in use.</returns>
    bool IsLocalPortInUse(int localPort);

    /// <summary>
    /// Starts all auto-start forwardings for a host on the given connection.
    /// </summary>
    /// <param name="connection">The SSH connection to use.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="hostId">The host ID to get auto-start profiles for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of handles for the started forwardings.</returns>
    Task<IReadOnlyList<PortForwardingHandle>> StartAutoStartForwardingsAsync(
        ISshConnection connection,
        Guid sessionId,
        Guid hostId,
        CancellationToken ct = default);
}

/// <summary>
/// Event arguments for port forwarding status changes.
/// </summary>
public sealed class PortForwardingStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// The forwarding that changed.
    /// </summary>
    public required ActivePortForwarding Forwarding { get; init; }

    /// <summary>
    /// The previous status.
    /// </summary>
    public PortForwardingStatus PreviousStatus { get; init; }

    /// <summary>
    /// The new status.
    /// </summary>
    public PortForwardingStatus NewStatus { get; init; }
}
