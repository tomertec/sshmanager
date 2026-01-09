namespace SshManager.Core.Models;

/// <summary>
/// Records a connection attempt to a host.
/// </summary>
public sealed class ConnectionHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The host that was connected to.
    /// </summary>
    public Guid HostId { get; set; }

    /// <summary>
    /// Navigation property to the host.
    /// </summary>
    public HostEntry? Host { get; set; }

    /// <summary>
    /// When the connection was initiated.
    /// </summary>
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the connection ended (null if still connected or unknown).
    /// </summary>
    public DateTimeOffset? DisconnectedAt { get; set; }

    /// <summary>
    /// Whether the connection was successful.
    /// </summary>
    public bool WasSuccessful { get; set; } = true;

    /// <summary>
    /// Error message if the connection failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
