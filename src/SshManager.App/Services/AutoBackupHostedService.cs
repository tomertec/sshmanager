using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;

namespace SshManager.App.Services;

/// <summary>
/// Background service that performs automatic backups at configured intervals.
/// </summary>
public class AutoBackupHostedService : BackgroundService, IBackgroundServiceHealth
{
    private readonly IBackupService _backupService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<AutoBackupHostedService> _logger;

    // Health tracking fields
    private readonly object _healthLock = new();
    private bool _isHealthy = true;
    private string _statusMessage = "Starting...";
    private string? _lastError;
    private DateTimeOffset? _lastSuccessfulRun;
    private DateTimeOffset? _lastErrorTime;
    private int _consecutiveFailures;
    private int _totalBackupsCreated;

    public AutoBackupHostedService(
        IBackupService backupService,
        ISettingsRepository settingsRepo,
        ILogger<AutoBackupHostedService> logger)
    {
        _backupService = backupService;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    #region IBackgroundServiceHealth Implementation

    public string ServiceName => "AutoBackup";

    public bool IsHealthy
    {
        get { lock (_healthLock) return _isHealthy; }
    }

    public string StatusMessage
    {
        get { lock (_healthLock) return _statusMessage; }
    }

    public string? LastError
    {
        get { lock (_healthLock) return _lastError; }
    }

    public DateTimeOffset? LastSuccessfulRun
    {
        get { lock (_healthLock) return _lastSuccessfulRun; }
    }

    public DateTimeOffset? LastErrorTime
    {
        get { lock (_healthLock) return _lastErrorTime; }
    }

    public int ConsecutiveFailures
    {
        get { lock (_healthLock) return _consecutiveFailures; }
    }

    public IReadOnlyDictionary<string, object> Metrics
    {
        get
        {
            lock (_healthLock)
            {
                return new Dictionary<string, object>
                {
                    ["TotalBackupsCreated"] = _totalBackupsCreated
                };
            }
        }
    }

    private void RecordSuccess(string statusMessage)
    {
        lock (_healthLock)
        {
            _isHealthy = true;
            _lastSuccessfulRun = DateTimeOffset.UtcNow;
            _consecutiveFailures = 0;
            _lastError = null;
            _statusMessage = statusMessage;
            _totalBackupsCreated++;
        }
    }

    private void RecordFailure(string error)
    {
        lock (_healthLock)
        {
            _consecutiveFailures++;
            _lastError = error;
            _lastErrorTime = DateTimeOffset.UtcNow;
            _statusMessage = $"Error: {error}";
            if (_consecutiveFailures >= 3)
            {
                _isHealthy = false;
            }
        }
    }

    private void UpdateStatus(string message)
    {
        lock (_healthLock)
        {
            _statusMessage = message;
        }
    }

    #endregion

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Auto backup service started");
        UpdateStatus("Waiting for initial delay...");

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
                    UpdateStatus("Disabled - waiting for configuration change");
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
                        UpdateStatus($"Waiting {waitTime.TotalMinutes:F0} minutes until next backup");
                        await Task.Delay(waitTime, stoppingToken);
                        continue;
                    }
                }

                _logger.LogInformation("Starting scheduled auto backup");
                UpdateStatus("Creating backup...");
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
                RecordSuccess($"Backup completed. Next in {interval.TotalMinutes:F0} minutes");
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto backup failed, will retry in 5 minutes");
                RecordFailure(ex.Message);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        UpdateStatus("Stopped");
        _logger.LogInformation("Auto backup service stopped");
    }
}
