using SshManager.Core.Models;

namespace SshManager.Data.Services;

/// <summary>
/// Provides caching for host entries to reduce database queries.
/// </summary>
public interface IHostCacheService
{
    /// <summary>
    /// Gets all host entries, using cache if available and not expired.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of host entries.</returns>
    Task<List<HostEntry>> GetAllHostsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets host counts grouped by group ID, using cache if available.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary mapping group ID (Guid.Empty for ungrouped) to host count.</returns>
    Task<Dictionary<Guid, int>> GetGroupCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cache, causing the next request to fetch fresh data.
    /// </summary>
    void Invalidate();

    /// <summary>
    /// Gets whether the cache is currently valid (not expired).
    /// </summary>
    bool IsCacheValid { get; }
}
