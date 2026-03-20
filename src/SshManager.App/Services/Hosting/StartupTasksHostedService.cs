using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Services;

namespace SshManager.App.Services.Hosting;

/// <summary>
/// Hosted service that runs miscellaneous startup tasks such as connection history cleanup.
/// </summary>
/// <remarks>
/// Note: Session recovery stays in App.xaml.cs since it requires the MainWindow reference.
/// </remarks>
public class StartupTasksHostedService : IHostedService
{
    private readonly IConnectionHistoryCleanupService _cleanupService;
    private readonly ILogger<StartupTasksHostedService> _logger;

    /// <summary>
    /// Directory used by <c>SessionViewModel</c> for restricted 1Password SSH key temp files.
    /// Must stay in sync with the path used in <c>SessionViewModel.CreateSecureTempKeyFileAsync</c>.
    /// </summary>
    private static readonly string TempKeyDirectory =
        Path.Combine(Path.GetTempPath(), "SshManager", "TempKeys");

    public StartupTasksHostedService(
        IConnectionHistoryCleanupService cleanupService,
        ILogger<StartupTasksHostedService> logger)
    {
        _cleanupService = cleanupService;
        _logger = logger;
    }

    /// <summary>
    /// Runs startup tasks including connection history cleanup and stale temp key file sweep.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running startup tasks...");

        // Cleanup old connection history entries
        await CleanupConnectionHistoryAsync();

        // Sweep any 1Password SSH key temp files left over from a previous crash
        await SweepStaleTempKeyFilesAsync();

        _logger.LogInformation("Startup tasks completed successfully");
    }

    /// <summary>
    /// No cleanup required on shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleans up old connection history entries based on the configured retention policy.
    /// </summary>
    private async Task CleanupConnectionHistoryAsync()
    {
        try
        {
            var deletedCount = await _cleanupService.CleanupOldEntriesAsync();

            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old connection history entries", deletedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup connection history - continuing with startup");
        }
    }

    /// <summary>
    /// Sweeps stale <c>sshm_op_*</c> temp files left in <c>%TEMP%\SshManager\TempKeys\</c> by a
    /// previous application run that crashed before session cleanup could delete them.
    /// Each file is overwritten with random bytes before deletion to prevent key material recovery.
    /// </summary>
    private async Task SweepStaleTempKeyFilesAsync()
    {
        if (!Directory.Exists(TempKeyDirectory))
            return;

        try
        {
            var staleFiles = Directory.GetFiles(TempKeyDirectory, "sshm_op_*");

            if (staleFiles.Length == 0)
                return;

            _logger.LogInformation(
                "Found {Count} stale 1Password temp key file(s) from a previous session — securely deleting",
                staleFiles.Length);

            var deletedCount = 0;
            var failedCount = 0;

            foreach (var filePath in staleFiles)
            {
                try
                {
                    await SecureDeleteFileAsync(filePath);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex, "Failed to securely delete stale temp key file: {Path}", filePath);
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Securely deleted {Count} stale temp key file(s)", deletedCount);
            }

            if (failedCount > 0)
            {
                _logger.LogWarning("{Count} stale temp key file(s) could not be deleted — they will be retried on next startup", failedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sweep stale temp key files from {Directory} — continuing with startup", TempKeyDirectory);
        }
    }

    /// <summary>
    /// Overwrites a file with random bytes then deletes it.
    /// </summary>
    private static async Task SecureDeleteFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var length = fileInfo.Length;

        if (length > 0)
        {
            var randomData = new byte[length];
            RandomNumberGenerator.Fill(randomData);
            await File.WriteAllBytesAsync(filePath, randomData);
        }

        File.Delete(filePath);
    }
}
