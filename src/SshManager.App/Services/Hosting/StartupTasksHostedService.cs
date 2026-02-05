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

    public StartupTasksHostedService(
        IConnectionHistoryCleanupService cleanupService,
        ILogger<StartupTasksHostedService> logger)
    {
        _cleanupService = cleanupService;
        _logger = logger;
    }

    /// <summary>
    /// Runs startup tasks including connection history cleanup.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running startup tasks...");

        // Cleanup old connection history entries
        await CleanupConnectionHistoryAsync();

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
}
