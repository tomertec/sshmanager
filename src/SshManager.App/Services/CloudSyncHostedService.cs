using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;

namespace SshManager.App.Services;

/// <summary>
/// Background service that performs automatic cloud sync at configured intervals.
/// Sync only runs when a passphrase has been set for the current session.
/// </summary>
public class CloudSyncHostedService : BackgroundService
{
    private readonly ICloudSyncService _cloudSyncService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<CloudSyncHostedService> _logger;

    private string? _sessionPassphrase;
    private readonly object _passphraseLock = new();

    public CloudSyncHostedService(
        ICloudSyncService cloudSyncService,
        ISettingsRepository settingsRepo,
        ILogger<CloudSyncHostedService> logger)
    {
        _cloudSyncService = cloudSyncService;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    /// <summary>
    /// Sets the passphrase for the current session, enabling background sync.
    /// </summary>
    /// <param name="passphrase">The sync passphrase.</param>
    public void SetSessionPassphrase(string? passphrase)
    {
        lock (_passphraseLock)
        {
            _sessionPassphrase = passphrase;
        }

        if (passphrase != null)
        {
            _logger.LogInformation("Session passphrase set, background sync enabled");
        }
        else
        {
            _logger.LogInformation("Session passphrase cleared, background sync disabled");
        }
    }

    /// <summary>
    /// Clears the session passphrase, disabling background sync.
    /// </summary>
    public void ClearSessionPassphrase()
    {
        SetSessionPassphrase(null);
    }

    /// <summary>
    /// Gets whether a session passphrase is currently set.
    /// </summary>
    public bool HasSessionPassphrase
    {
        get
        {
            lock (_passphraseLock)
            {
                return _sessionPassphrase != null;
            }
        }
    }

    /// <summary>
    /// Triggers an immediate sync if passphrase is available.
    /// </summary>
    public async Task TriggerSyncAsync(CancellationToken ct = default)
    {
        string? passphrase;
        lock (_passphraseLock)
        {
            passphrase = _sessionPassphrase;
        }

        if (passphrase == null)
        {
            _logger.LogWarning("Cannot trigger sync: no session passphrase set");
            return;
        }

        await _cloudSyncService.SyncAsync(passphrase, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cloud sync background service started");

        // Initial delay to allow application to fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await _settingsRepo.GetAsync(stoppingToken);

                if (!settings.EnableCloudSync)
                {
                    // Check again in 1 minute if disabled
                    _logger.LogDebug("Cloud sync is disabled, checking again in 1 minute");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                string? passphrase;
                lock (_passphraseLock)
                {
                    passphrase = _sessionPassphrase;
                }

                if (passphrase == null)
                {
                    // No passphrase set, check again in 1 minute
                    _logger.LogDebug("No session passphrase set, checking again in 1 minute");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                var interval = TimeSpan.FromMinutes(settings.SyncIntervalMinutes);

                // Check if enough time has passed since last sync
                if (settings.LastSyncTime.HasValue)
                {
                    var timeSinceLastSync = DateTimeOffset.UtcNow - settings.LastSyncTime.Value;
                    if (timeSinceLastSync < interval)
                    {
                        var waitTime = interval - timeSinceLastSync;
                        _logger.LogDebug("Waiting {WaitTime} until next sync", waitTime);
                        await Task.Delay(waitTime, stoppingToken);
                        continue;
                    }
                }

                _logger.LogInformation("Starting scheduled cloud sync");
                await _cloudSyncService.SyncAsync(passphrase, stoppingToken);

                _logger.LogInformation("Cloud sync completed, next sync in {Interval}", interval);
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                _logger.LogError(ex, "Cloud sync failed: invalid passphrase. Clearing session passphrase.");
                ClearSessionPassphrase();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloud sync failed, will retry in 5 minutes");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Cloud sync background service stopped");
    }
}
