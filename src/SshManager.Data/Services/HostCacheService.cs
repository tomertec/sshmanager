using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.Data.Services;

/// <summary>
/// Provides memory caching for host entries to reduce database queries.
/// Thread-safe implementation with automatic cache expiration.
/// </summary>
public sealed class HostCacheService : IHostCacheService, IDisposable
{
    private readonly IHostRepository _repo;
    private readonly ILogger<HostCacheService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private List<HostEntry>? _cachedHosts;
    private Dictionary<Guid, int>? _cachedGroupCounts;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of the HostCacheService.
    /// </summary>
    /// <param name="repo">The host repository to query.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public HostCacheService(IHostRepository repo, ILogger<HostCacheService>? logger = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _logger = logger ?? NullLogger<HostCacheService>.Instance;
    }

    /// <inheritdoc />
    public bool IsCacheValid => _cachedHosts != null && DateTimeOffset.UtcNow < _cacheExpiry;

    /// <inheritdoc />
    public async Task<List<HostEntry>> GetAllHostsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(ct);
        try
        {
            // Return cached data if valid
            if (_cachedHosts != null && DateTimeOffset.UtcNow < _cacheExpiry)
            {
                _logger.LogDebug("Returning {Count} hosts from cache", _cachedHosts.Count);
                return _cachedHosts.ToList();
            }

            // Fetch fresh data
            _logger.LogDebug("Cache miss - fetching hosts from database");
            _cachedHosts = await _repo.GetAllAsync(ct);
            _cachedGroupCounts = null; // Invalidate group counts when hosts change
            _cacheExpiry = DateTimeOffset.UtcNow.Add(CacheTtl);

            _logger.LogDebug("Cached {Count} hosts with expiry at {Expiry}",
                _cachedHosts.Count, _cacheExpiry);

            return _cachedHosts.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<Guid, int>> GetGroupCountsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(ct);
        try
        {
            // If we have cached group counts and host cache is still valid, use it
            if (_cachedGroupCounts != null && _cachedHosts != null && DateTimeOffset.UtcNow < _cacheExpiry)
            {
                _logger.LogDebug("Returning group counts from cache");
                return new Dictionary<Guid, int>(_cachedGroupCounts);
            }

            // Ensure we have hosts cached first
            if (_cachedHosts == null || DateTimeOffset.UtcNow >= _cacheExpiry)
            {
                _logger.LogDebug("Cache miss for group counts - fetching hosts from database");
                _cachedHosts = await _repo.GetAllAsync(ct);
                _cacheExpiry = DateTimeOffset.UtcNow.Add(CacheTtl);
            }

            // Compute group counts from cached hosts (use Guid.Empty for ungrouped)
            _cachedGroupCounts = _cachedHosts
                .GroupBy(h => h.GroupId ?? Guid.Empty)
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogDebug("Computed group counts for {Count} groups", _cachedGroupCounts.Count);

            return new Dictionary<Guid, int>(_cachedGroupCounts);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        if (_disposed) return;

        _lock.Wait();
        try
        {
            _cachedHosts = null;
            _cachedGroupCounts = null;
            _cacheExpiry = DateTimeOffset.MinValue;
            _logger.LogDebug("Host cache invalidated");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Disposes resources used by the cache service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cachedHosts = null;
        _cachedGroupCounts = null;
        _lock.Dispose();
    }
}
