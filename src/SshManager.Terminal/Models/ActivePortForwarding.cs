using SshManager.Core.Models;

namespace SshManager.Terminal.Models;

/// <summary>
/// Status of a port forwarding.
/// </summary>
public enum PortForwardingStatus
{
    /// <summary>
    /// The forwarding is being started.
    /// </summary>
    Starting,

    /// <summary>
    /// The forwarding is active and ready for connections.
    /// </summary>
    Active,

    /// <summary>
    /// The forwarding failed to start or encountered an error.
    /// </summary>
    Failed,

    /// <summary>
    /// The forwarding was stopped.
    /// </summary>
    Stopped
}

/// <summary>
/// Represents an active port forwarding with runtime status information.
/// </summary>
public sealed class ActivePortForwarding
{
    /// <summary>
    /// Unique identifier for this forwarding instance.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The session ID this forwarding is associated with.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// The profile that defines this forwarding.
    /// </summary>
    public required PortForwardingProfile Profile { get; init; }

    /// <summary>
    /// Current status of the forwarding.
    /// </summary>
    public PortForwardingStatus Status { get; set; } = PortForwardingStatus.Starting;

    /// <summary>
    /// Error message if status is Failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When this forwarding was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Total bytes transferred through this forwarding.
    /// </summary>
    public long BytesTransferred { get; set; }

    /// <summary>
    /// Number of active connections through this forwarding.
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// The handle for managing this forwarding.
    /// </summary>
    internal PortForwardingHandle? Handle { get; set; }

    /// <summary>
    /// Gets a display-friendly description of this forwarding.
    /// </summary>
    public string GetDisplayDescription()
    {
        return Profile.ForwardingType switch
        {
            PortForwardingType.LocalForward =>
                $"{Profile.LocalBindAddress}:{Profile.LocalPort} → {Profile.RemoteHost}:{Profile.RemotePort}",

            PortForwardingType.RemoteForward =>
                $"Remote:{Profile.RemotePort} → {Profile.LocalBindAddress}:{Profile.LocalPort}",

            PortForwardingType.DynamicForward =>
                $"{Profile.LocalBindAddress}:{Profile.LocalPort} (SOCKS5)",

            _ => $"Unknown forwarding type"
        };
    }

    /// <summary>
    /// Gets a display-friendly status string.
    /// </summary>
    public string GetStatusDisplay()
    {
        return Status switch
        {
            PortForwardingStatus.Starting => "Starting...",
            PortForwardingStatus.Active => "Active",
            PortForwardingStatus.Failed => ErrorMessage ?? "Failed",
            PortForwardingStatus.Stopped => "Stopped",
            _ => "Unknown"
        };
    }
}
