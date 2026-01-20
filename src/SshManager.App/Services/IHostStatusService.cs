namespace SshManager.App.Services;

/// <summary>
/// Represents the overall status level of a host.
/// </summary>
public enum HostStatusLevel
{
    /// <summary>
    /// Status unknown (not yet checked).
    /// </summary>
    Unknown,

    /// <summary>
    /// Host is offline (both ping and TCP failed).
    /// </summary>
    Offline,

    /// <summary>
    /// Host is partially reachable (ping works but TCP port closed, or high latency).
    /// </summary>
    Degraded,

    /// <summary>
    /// Host is online and fully reachable.
    /// </summary>
    Online
}

/// <summary>
/// Status information for a host with detailed connectivity metrics.
/// </summary>
public sealed record HostStatus
{
    /// <summary>
    /// Gets the host ID this status is for.
    /// </summary>
    public Guid HostId { get; init; }

    /// <summary>
    /// Gets whether the host is considered online.
    /// </summary>
    public bool IsOnline { get; init; }

    /// <summary>
    /// Gets the ICMP ping latency in milliseconds, if ping succeeded.
    /// </summary>
    public int? PingLatencyMs { get; init; }

    /// <summary>
    /// Gets whether the SSH/target port is open and accepting connections.
    /// </summary>
    public bool IsPortOpen { get; init; }

    /// <summary>
    /// Gets the TCP connection latency in milliseconds, if port check succeeded.
    /// </summary>
    public int? TcpLatencyMs { get; init; }

    /// <summary>
    /// Gets the overall latency (prefers TCP latency if available, otherwise ping).
    /// </summary>
    public TimeSpan? Latency => TcpLatencyMs.HasValue
        ? TimeSpan.FromMilliseconds(TcpLatencyMs.Value)
        : PingLatencyMs.HasValue
            ? TimeSpan.FromMilliseconds(PingLatencyMs.Value)
            : null;

    /// <summary>
    /// Gets when this status was last checked.
    /// </summary>
    public DateTimeOffset? LastChecked { get; init; }

    /// <summary>
    /// Gets whether the host is reachable by any means (ping or TCP).
    /// </summary>
    public bool IsReachable => PingLatencyMs.HasValue || IsPortOpen;

    /// <summary>
    /// Gets the overall status level based on connectivity metrics.
    /// </summary>
    public HostStatusLevel Level
    {
        get
        {
            if (!LastChecked.HasValue)
            {
                return HostStatusLevel.Unknown;
            }

            if (!IsReachable)
            {
                return HostStatusLevel.Offline;
            }

            // Degraded if ping works but port is closed, or latency is high (>500ms)
            if ((PingLatencyMs.HasValue && !IsPortOpen) ||
                (Latency.HasValue && Latency.Value.TotalMilliseconds > 500))
            {
                return HostStatusLevel.Degraded;
            }

            return HostStatusLevel.Online;
        }
    }

    /// <summary>
    /// Creates a simple HostStatus from basic parameters (for backward compatibility).
    /// </summary>
    public static HostStatus Create(bool isOnline, TimeSpan? latency, DateTimeOffset? lastChecked)
    {
        return new HostStatus
        {
            IsOnline = isOnline,
            PingLatencyMs = latency.HasValue ? (int)latency.Value.TotalMilliseconds : null,
            IsPortOpen = isOnline,
            LastChecked = lastChecked
        };
    }
}

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
