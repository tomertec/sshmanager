using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;

namespace SshManager.App.Services;

/// <summary>
/// Background service that performs automatic backups at configured intervals.
/// </summary>
public class AutoBackupHostedService : BackgroundService
{
    private readonly IBackupService _backupService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<AutoBackupHostedService> _logger;

    public AutoBackupHostedService(
        IBackupService backupService,
        ISettingsRepository settingsRepo,
        ILogger<AutoBackupHostedService> logger)
    {
        _backupService = backupService;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Auto backup service started");

        // Initial delay to allow application to fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await _settingsRepo.GetAsync(stoppingToken);

                if (!settings.EnableAutoBackup)
                {
                    // Check again in 1 minute if disabled
                    _logger.LogDebug("Auto backup is disabled, checking again in 1 minute");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                var interval = TimeSpan.FromMinutes(settings.BackupIntervalMinutes);

                // Check if enough time has passed since last backup
                if (settings.LastAutoBackupTime.HasValue)
                {
                    var timeSinceLastBackup = DateTimeOffset.UtcNow - settings.LastAutoBackupTime.Value;
                    if (timeSinceLastBackup < interval)
                    {
                        var waitTime = interval - timeSinceLastBackup;
                        _logger.LogDebug("Waiting {WaitTime} until next backup", waitTime);
                        await Task.Delay(waitTime, stoppingToken);
                        continue;
                    }
                }

                _logger.LogInformation("Starting scheduled auto backup");
                await _backupService.CreateBackupAsync(stoppingToken);

                // Update last backup time
                settings.LastAutoBackupTime = DateTimeOffset.UtcNow;
                await _settingsRepo.UpdateAsync(settings, stoppingToken);

                // Cleanup old backups
                if (settings.MaxBackupCount > 0)
                {
                    var deleted = await _backupService.CleanupOldBackupsAsync(
                        settings.MaxBackupCount, stoppingToken);
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Cleaned up {Count} old backups", deleted);
                    }
                }

                _logger.LogInformation("Auto backup completed, next backup in {Interval}", interval);
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto backup failed, will retry in 5 minutes");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Auto backup service stopped");
    }
}
