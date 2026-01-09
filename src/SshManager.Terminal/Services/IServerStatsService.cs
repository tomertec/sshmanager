using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Server statistics retrieved via SSH.
/// </summary>
public record ServerStats(double? CpuUsage, double? MemoryUsage, double? DiskUsage, TimeSpan? ServerUptime);

/// <summary>
/// Service for collecting server resource statistics via SSH commands.
/// </summary>
public interface IServerStatsService
{
    /// <summary>
    /// Gets the current server statistics by executing SSH commands.
    /// </summary>
    /// <param name="connection">The SSH connection to use.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Server statistics, or null values if collection failed.</returns>
    Task<ServerStats> GetStatsAsync(ISshConnection connection, CancellationToken ct = default);
}
