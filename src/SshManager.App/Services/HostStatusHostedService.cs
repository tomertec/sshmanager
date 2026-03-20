using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;

namespace SshManager.App.Services;

/// <summary>
/// Background service that periodically checks host status via ping.
/// </summary>
public class HostStatusHostedService : BackgroundService
{
    private readonly IHostStatusService _hostStatusService;
    private readonly IHostRepository _hostRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly ILogger<HostStatusHostedService> _logger;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SchedulerInterval = TimeSpan.FromSeconds(5);
    private const int DefaultGroupIntervalSeconds = 30;
    private const int MinimumGroupIntervalSeconds = 5;
    private readonly HashSet<Guid> _registeredHostIds = [];

    // In-memory cache of host endpoints to avoid querying the DB on every 5-second tick.
    // Keyed by host ID; holds the data needed to register/compare hosts.
    private sealed record CachedHostEndpoint(Guid HostId, string Hostname, int Port, Guid? GroupId);
    private sealed record CachedGroupInterval(Guid GroupId, int IntervalSeconds);

    private List<CachedHostEndpoint> _cachedHosts = [];
    private Dictionary<Guid, int> _cachedGroupIntervals = [];
    private volatile bool _cacheInvalid = true;

    public HostStatusHostedService(
        IHostStatusService hostStatusService,
        IHostRepository hostRepo,
        IGroupRepository groupRepo,
        ILogger<HostStatusHostedService> logger)
    {
        _hostStatusService = hostStatusService;
        _hostRepo = hostRepo;
        _groupRepo = groupRepo;
        _logger = logger;
    }

    /// <summary>
    /// Signals that the host/group data has changed and the cache should be refreshed
    /// on the next scheduler tick.
    /// </summary>
    public void InvalidateCache()
    {
        _cacheInvalid = true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Host status monitoring service started");

        // Initial delay to allow application to fully start
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Only re-query the database when the cache has been invalidated.
                // This avoids loading all hosts + groups on every 5-second scheduler tick.
                if (_cacheInvalid)
                {
                    await RefreshCacheAsync(stoppingToken);
                    _cacheInvalid = false;
                }

                // Register or unregister hosts using the in-memory cache
                var currentHostIds = _cachedHosts.Select(h => h.HostId).ToHashSet();
                foreach (var removedId in _registeredHostIds.Except(currentHostIds).ToList())
                {
                    _hostStatusService.UnregisterHost(removedId);
                    _registeredHostIds.Remove(removedId);
                }

                foreach (var cached in _cachedHosts)
                {
                    var intervalSeconds = DefaultGroupIntervalSeconds;
                    if (cached.GroupId.HasValue &&
                        _cachedGroupIntervals.TryGetValue(cached.GroupId.Value, out var groupInterval))
                    {
                        intervalSeconds = groupInterval;
                    }

                    _hostStatusService.RegisterHost(cached.HostId, cached.Hostname, cached.Port, intervalSeconds);
                    _registeredHostIds.Add(cached.HostId);
                }

                _logger.LogDebug("Checking status of {HostCount} hosts", _cachedHosts.Count);

                // Check all hosts
                await _hostStatusService.CheckAllHostsAsync(stoppingToken);

                // Wait for next scheduler tick
                await Task.Delay(SchedulerInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during host status check, will retry in {Interval}", SchedulerInterval);
                await Task.Delay(SchedulerInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Host status monitoring service stopped");
    }

    /// <summary>
    /// Refreshes the in-memory cache from the database. Called only when <see cref="_cacheInvalid"/>
    /// is true, which is set initially and whenever <see cref="InvalidateCache"/> is called.
    /// </summary>
    private async Task RefreshCacheAsync(CancellationToken ct)
    {
        _logger.LogDebug("Refreshing host status cache from database");

        var hosts = await _hostRepo.GetAllAsync(ct);
        var groups = await _groupRepo.GetAllAsync(ct);

        _cachedHosts = hosts
            .Select(h => new CachedHostEndpoint(h.Id, h.Hostname, h.Port, h.GroupId))
            .ToList();

        _cachedGroupIntervals = groups.ToDictionary(
            g => g.Id,
            g => NormalizeIntervalSeconds(g.StatusCheckIntervalSeconds));
    }

    private static int NormalizeIntervalSeconds(int intervalSeconds)
    {
        if (intervalSeconds <= 0)
        {
            return DefaultGroupIntervalSeconds;
        }

        return Math.Max(intervalSeconds, MinimumGroupIntervalSeconds);
    }
}
