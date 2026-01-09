namespace SshManager.Terminal.Models;

/// <summary>
/// Handle returned when starting a port forwarding.
/// Used to identify and manage active forwardings.
/// </summary>
public sealed class PortForwardingHandle
{
    /// <summary>
    /// Unique identifier for this forwarding instance.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The session ID this forwarding is associated with.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// The profile ID that defines this forwarding.
    /// </summary>
    public Guid ProfileId { get; init; }

    /// <summary>
    /// When this forwarding was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Action to stop this forwarding.
    /// </summary>
    internal Action? StopAction { get; init; }

    /// <summary>
    /// The underlying SSH.NET forwarded port (for cleanup).
    /// </summary>
    internal IDisposable? ForwardedPort { get; init; }
}
