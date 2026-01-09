namespace SshManager.App.Services;

/// <summary>
/// Status information for a host.
/// </summary>
public record HostStatus(bool IsOnline, TimeSpan? Latency, DateTimeOffset? LastChecked);

/// <summary>
/// Service for checking host online/offline status via ICMP ping with TCP fallback.
/// </summary>
public interface IHostStatusService
{
    /// <summary>
    /// Gets the current status for a host by ID.
    /// </summary>
    HostStatus? GetStatus(Guid hostId);

    /// <summary>
    /// Gets all current host statuses.
    /// </summary>
    IReadOnlyDictionary<Guid, HostStatus> GetAllStatuses();

    /// <summary>
    /// Checks the status of a specific host immediately.
    /// </summary>
    Task<HostStatus> CheckHostAsync(Guid hostId, string hostname, int port, CancellationToken ct = default);

    /// <summary>
    /// Checks all registered hosts.
    /// </summary>
    Task CheckAllHostsAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers a host for status monitoring.
    /// </summary>
    void RegisterHost(Guid hostId, string hostname, int port, int checkIntervalSeconds);

    /// <summary>
    /// Unregisters a host from status monitoring.
    /// </summary>
    void UnregisterHost(Guid hostId);

    /// <summary>
    /// Clears all registered hosts.
    /// </summary>
    void ClearHosts();

    /// <summary>
    /// Event raised when a host's status changes.
    /// </summary>
    event EventHandler<HostStatusChangedEventArgs>? StatusChanged;
}

/// <summary>
/// Event arguments for host status changes.
/// </summary>
public class HostStatusChangedEventArgs : EventArgs
{
    public Guid HostId { get; }
    public HostStatus Status { get; }

    public HostStatusChangedEventArgs(Guid hostId, HostStatus status)
    {
        HostId = hostId;
        Status = status;
    }
}
