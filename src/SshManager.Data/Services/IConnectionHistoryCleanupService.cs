namespace SshManager.Data.Services;

/// <summary>
/// Service for cleaning up old connection history entries based on retention policy.
/// </summary>
public interface IConnectionHistoryCleanupService
{
    /// <summary>
    /// Removes connection history entries older than the configured retention period.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of entries deleted</returns>
    Task<int> CleanupOldEntriesAsync(CancellationToken ct = default);
}
