using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing connection history.
/// </summary>
public interface IConnectionHistoryRepository
{
    Task<List<ConnectionHistory>> GetRecentAsync(int count = 20, CancellationToken ct = default);
    Task<List<ConnectionHistory>> GetByHostAsync(Guid hostId, CancellationToken ct = default);
    Task<List<HostEntry>> GetRecentUniqueHostsAsync(int count = 5, CancellationToken ct = default);
    Task AddAsync(ConnectionHistory entry, CancellationToken ct = default);
    Task UpdateAsync(ConnectionHistory entry, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
    Task ClearOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
    Task<int> CountOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
    
    /// <summary>
    /// Gets connection statistics for a specific host.
    /// </summary>
    Task<HostConnectionStats> GetHostStatsAsync(Guid hostId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets connection statistics for all hosts.
    /// </summary>
    Task<Dictionary<Guid, HostConnectionStats>> GetAllHostStatsAsync(CancellationToken ct = default);
}
