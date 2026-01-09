namespace SshManager.App.Services;

/// <summary>
/// Service for synchronizing host configurations via encrypted cloud storage.
/// </summary>
public interface ICloudSyncService
{
    /// <summary>
    /// Indicates whether cloud sync is available (e.g., OneDrive is installed).
    /// </summary>
    bool IsCloudSyncAvailable { get; }

    /// <summary>
    /// Current sync status.
    /// </summary>
    SyncStatus Status { get; }

    /// <summary>
    /// Checks whether cloud sync is currently enabled in settings.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if cloud sync is enabled; otherwise, false.</returns>
    Task<bool> GetIsCloudSyncEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the timestamp of the last successful sync operation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The last sync time, or null if never synced.</returns>
    Task<DateTimeOffset?> GetLastSyncTimeAsync(CancellationToken ct = default);

    /// <summary>
    /// Event raised when sync status changes.
    /// </summary>
    event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Sets up cloud sync with the specified passphrase.
    /// Creates the sync folder and initial sync file if needed.
    /// </summary>
    /// <param name="passphrase">The encryption passphrase.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetupSyncAsync(string passphrase, CancellationToken ct = default);

    /// <summary>
    /// Performs a sync operation using the provided passphrase.
    /// </summary>
    /// <param name="passphrase">The encryption passphrase.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SyncAsync(string passphrase, CancellationToken ct = default);

    /// <summary>
    /// Disables cloud sync and optionally removes the sync file.
    /// </summary>
    /// <param name="removeSyncFile">Whether to delete the encrypted sync file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DisableSyncAsync(bool removeSyncFile = false, CancellationToken ct = default);

    /// <summary>
    /// Validates if the passphrase can decrypt the existing sync file.
    /// </summary>
    /// <param name="passphrase">The passphrase to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the passphrase is valid; otherwise, false.</returns>
    Task<bool> ValidatePassphraseAsync(string passphrase, CancellationToken ct = default);

    /// <summary>
    /// Checks if a sync file exists in the configured location.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the sync file exists; otherwise, false.</returns>
    Task<bool> SyncFileExistsAsync(CancellationToken ct = default);
}

/// <summary>
/// Status of the cloud sync service.
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// Sync is not enabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// Sync is enabled and idle.
    /// </summary>
    Idle,

    /// <summary>
    /// Sync operation is in progress.
    /// </summary>
    Syncing,

    /// <summary>
    /// Last sync operation encountered an error.
    /// </summary>
    Error,

    /// <summary>
    /// A sync conflict was detected that requires user intervention.
    /// </summary>
    Conflict
}

/// <summary>
/// Event arguments for sync status changes.
/// </summary>
public class SyncStatusChangedEventArgs : EventArgs
{
    public SyncStatus Status { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
}
