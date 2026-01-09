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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Host status monitoring service started");

        // Initial delay to allow application to fully start
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Load all hosts and register them
                var hosts = await _hostRepo.GetAllAsync(stoppingToken);
                var groups = await _groupRepo.GetAllAsync(stoppingToken);
                var groupIntervals = groups.ToDictionary(
                    g => g.Id,
                    g => NormalizeIntervalSeconds(g.StatusCheckIntervalSeconds));

                var currentHostIds = hosts.Select(h => h.Id).ToHashSet();
                foreach (var removedId in _registeredHostIds.Except(currentHostIds).ToList())
                {
                    _hostStatusService.UnregisterHost(removedId);
                    _registeredHostIds.Remove(removedId);
                }

                foreach (var host in hosts)
                {
                    var intervalSeconds = DefaultGroupIntervalSeconds;
                    if (host.GroupId.HasValue &&
                        groupIntervals.TryGetValue(host.GroupId.Value, out var groupInterval))
                    {
                        intervalSeconds = groupInterval;
                    }

                    _hostStatusService.RegisterHost(host.Id, host.Hostname, host.Port, intervalSeconds);
                    _registeredHostIds.Add(host.Id);
                }

                _logger.LogDebug("Checking status of {HostCount} hosts", hosts.Count);

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

    private static int NormalizeIntervalSeconds(int intervalSeconds)
    {
        if (intervalSeconds <= 0)
        {
            return DefaultGroupIntervalSeconds;
        }

        return Math.Max(intervalSeconds, MinimumGroupIntervalSeconds);
    }
}
