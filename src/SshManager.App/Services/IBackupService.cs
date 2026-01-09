namespace SshManager.App.Services;

/// <summary>
/// Service for managing automatic and manual backups of host configurations.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new backup file.
    /// </summary>
    Task<BackupInfo> CreateBackupAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all available backups.
    /// </summary>
    Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Restores data from a backup file.
    /// </summary>
    /// <returns>The number of hosts and groups restored.</returns>
    Task<(int HostCount, int GroupCount)> RestoreBackupAsync(string backupPath, CancellationToken ct = default);

    /// <summary>
    /// Deletes backups exceeding the maximum count.
    /// </summary>
    /// <returns>The number of backups deleted.</returns>
    Task<int> CleanupOldBackupsAsync(int keepCount, CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific backup file.
    /// </summary>
    Task DeleteBackupAsync(string backupPath, CancellationToken ct = default);

    /// <summary>
    /// Gets the backup directory path.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The backup directory path.</returns>
    Task<string> GetBackupDirectoryAsync(CancellationToken ct = default);

    /// <summary>
    /// Opens the backup directory in File Explorer.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task OpenBackupDirectoryAsync(CancellationToken ct = default);
}

/// <summary>
/// Information about a backup file.
/// </summary>
public class BackupInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public long FileSizeBytes { get; set; }
    public int HostCount { get; set; }
    public int GroupCount { get; set; }

    /// <summary>
    /// Human-readable file size.
    /// </summary>
    public string FileSizeFormatted => FormatFileSize(FileSizeBytes);

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
