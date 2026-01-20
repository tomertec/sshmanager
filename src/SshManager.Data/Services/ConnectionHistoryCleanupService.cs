using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;

namespace SshManager.Data.Services;

/// <summary>
/// Service implementation for cleaning up old connection history entries based on retention policy.
/// </summary>
public sealed class ConnectionHistoryCleanupService : IConnectionHistoryCleanupService
{
    private readonly IConnectionHistoryRepository _historyRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<ConnectionHistoryCleanupService> _logger;

    public ConnectionHistoryCleanupService(
        IConnectionHistoryRepository historyRepository,
        ISettingsRepository settingsRepository,
        ILogger<ConnectionHistoryCleanupService> logger)
    {
        _historyRepository = historyRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Removes connection history entries older than the configured retention period.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of entries deleted</returns>
    public async Task<int> CleanupOldEntriesAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await _settingsRepository.GetAsync(ct);

            // If retention is 0, keep history forever
            if (settings.ConnectionHistoryRetentionDays == 0)
            {
                _logger.LogDebug("Connection history retention is disabled (0 days), skipping cleanup");
                return 0;
            }

            // Calculate cutoff date
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-settings.ConnectionHistoryRetentionDays);

            _logger.LogInformation(
                "Cleaning up connection history older than {Days} days (cutoff: {CutoffDate})",
                settings.ConnectionHistoryRetentionDays,
                cutoffDate);

            // Get count before deletion for logging
            var entriesToDelete = await _historyRepository.CountOlderThanAsync(cutoffDate, ct);

            if (entriesToDelete == 0)
            {
                _logger.LogDebug("No connection history entries to clean up");
                return 0;
            }

            // Delete old entries
            await _historyRepository.ClearOlderThanAsync(cutoffDate, ct);

            _logger.LogInformation(
                "Successfully cleaned up {Count} connection history entries older than {Days} days",
                entriesToDelete,
                settings.ConnectionHistoryRetentionDays);

            return entriesToDelete;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup connection history");
            throw;
        }
    }
}
