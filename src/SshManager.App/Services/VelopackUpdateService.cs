using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace SshManager.App.Services;

/// <summary>
/// Velopack-based implementation of the update service.
/// Handles checking for updates, downloading, and applying them.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly UpdateManager _updateManager;
    private readonly ILogger<VelopackUpdateService> _logger;
    private bool _isCheckingForUpdate;
    private bool _isDownloadingUpdate;
    private Velopack.UpdateInfo? _velopackUpdateInfo; // Store the Velopack update info

    public bool IsCheckingForUpdate => _isCheckingForUpdate;
    public bool IsDownloadingUpdate => _isDownloadingUpdate;

    public VelopackUpdateService(ILogger<VelopackUpdateService> logger)
    {
        _logger = logger;

        // Initialize UpdateManager with GitHub releases source
        // TODO: Replace with your actual GitHub repository URL
        var source = new GithubSource(
            repoUrl: "https://github.com/tomertec/sshmanager",
            accessToken: null, // Public repository
            prerelease: false); // Set to true to include pre-release versions

        _updateManager = new UpdateManager(source);
    }

    public string GetCurrentVersion()
    {
        try
        {
            var currentVersion = _updateManager.CurrentVersion;
            return currentVersion?.ToString() ?? "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get current version");
            return "Unknown";
        }
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (_isCheckingForUpdate)
        {
            _logger.LogDebug("Update check already in progress");
            return null;
        }

        _isCheckingForUpdate = true;
        try
        {
            _logger.LogInformation("Checking for updates...");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                _logger.LogInformation("No updates available");
                return null;
            }

            _logger.LogInformation(
                "Update available: v{NewVersion} (current: v{CurrentVersion})",
                updateInfo.TargetFullRelease.Version,
                _updateManager.CurrentVersion);

            // Store the Velopack update info for later download
            _velopackUpdateInfo = updateInfo;

            return new UpdateInfo(
                Version: updateInfo.TargetFullRelease.Version.ToString(),
                ReleaseNotes: null, // Velopack doesn't expose release notes directly
                DownloadSizeBytes: updateInfo.TargetFullRelease.Size,
                PublishedAt: null); // Velopack doesn't expose published date
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return null;
        }
        finally
        {
            _isCheckingForUpdate = false;
        }
    }

    public async Task DownloadUpdateAsync(
        UpdateInfo updateInfo,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (_isDownloadingUpdate)
        {
            _logger.LogWarning("Update download already in progress");
            return;
        }

        if (_velopackUpdateInfo == null)
        {
            throw new InvalidOperationException("No update available. Call CheckForUpdateAsync first.");
        }

        _isDownloadingUpdate = true;
        try
        {
            _logger.LogInformation("Downloading update v{Version}", updateInfo.Version);

            // Convert progress from 0-100 int to 0-100 int for Velopack callback
            Action<int>? velopackProgress = progress != null
                ? p => progress.Report(p)
                : null;

            await _updateManager.DownloadUpdatesAsync(_velopackUpdateInfo, velopackProgress);

            _logger.LogInformation("Update download completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update download cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            throw;
        }
        finally
        {
            _isDownloadingUpdate = false;
        }
    }

    public async Task ApplyUpdateAndRestartAsync()
    {
        try
        {
            if (_velopackUpdateInfo == null)
            {
                throw new InvalidOperationException("No update available. Download an update first.");
            }

            _logger.LogInformation("Applying update and restarting application");

            // This will apply the update and restart the application
            // The method will not return - the app will be restarted
            _updateManager.ApplyUpdatesAndRestart(_velopackUpdateInfo.TargetFullRelease);

            // Add a small delay to ensure the restart process begins
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update and restart");
            throw;
        }
    }
}
