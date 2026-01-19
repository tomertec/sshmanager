namespace SshManager.App.Services;

/// <summary>
/// Service for checking and applying application updates using Velopack.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Checks if an update is available.
    /// </summary>
    /// <returns>Update info if available, null otherwise.</returns>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads and prepares an update for installation.
    /// </summary>
    /// <param name="updateInfo">The update to download.</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="ct">Cancellation token.</param>
    Task DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// </summary>
    /// <remarks>
    /// This method will not return - the application will restart.
    /// </remarks>
    Task ApplyUpdateAndRestartAsync();

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    string GetCurrentVersion();

    /// <summary>
    /// Gets whether an update check is currently in progress.
    /// </summary>
    bool IsCheckingForUpdate { get; }

    /// <summary>
    /// Gets whether an update download is currently in progress.
    /// </summary>
    bool IsDownloadingUpdate { get; }
}

/// <summary>
/// Information about an available update.
/// </summary>
public sealed record UpdateInfo(
    string Version,
    string? ReleaseNotes,
    long DownloadSizeBytes,
    DateTimeOffset? PublishedAt);
