using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SshManager.App.Services.Hosting;

/// <summary>
/// Hosted service that manages the system tray icon lifecycle.
/// </summary>
public class SystemTrayHostedService : IHostedService
{
    private readonly ISystemTrayService _systemTrayService;
    private readonly ILogger<SystemTrayHostedService> _logger;

    public SystemTrayHostedService(
        ISystemTrayService systemTrayService,
        ILogger<SystemTrayHostedService> logger)
    {
        _systemTrayService = systemTrayService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the system tray icon.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing system tray...");

        try
        {
            _systemTrayService.Initialize();
            _logger.LogDebug("System tray service initialized");

            _logger.LogInformation("System tray initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize system tray");
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the system tray icon on shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Disposing system tray...");

        try
        {
            _systemTrayService.Dispose();
            _logger.LogDebug("System tray disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing system tray");
        }

        return Task.CompletedTask;
    }
}
