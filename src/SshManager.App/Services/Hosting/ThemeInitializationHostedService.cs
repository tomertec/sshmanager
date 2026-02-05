using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;
using SshManager.Terminal.Services;

namespace SshManager.App.Services.Hosting;

/// <summary>
/// Hosted service that initializes application and terminal themes on startup.
/// </summary>
/// <remarks>
/// This service implements <see cref="IHostedService"/> (not BackgroundService) because
/// theme initialization must complete before the UI is displayed.
/// </remarks>
public class ThemeInitializationHostedService : IHostedService
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ITerminalThemeService _terminalThemeService;
    private readonly ILogger<ThemeInitializationHostedService> _logger;

    public ThemeInitializationHostedService(
        ISettingsRepository settingsRepo,
        ITerminalThemeService terminalThemeService,
        ILogger<ThemeInitializationHostedService> logger)
    {
        _settingsRepo = settingsRepo;
        _terminalThemeService = terminalThemeService;
        _logger = logger;
    }

    /// <summary>
    /// Loads and applies application and terminal themes from settings.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing themes...");

        try
        {
            // Load settings
            var settings = await _settingsRepo.GetAsync(cancellationToken);
            _logger.LogDebug("Application settings loaded");

            // Apply application theme from settings
            App.ApplyApplicationTheme(settings.Theme);
            _logger.LogDebug("Application theme set to {Theme}", settings.Theme);

            // Load custom terminal themes
            await _terminalThemeService.LoadCustomThemesAsync();
            _logger.LogDebug("Custom terminal themes loaded");

            _logger.LogInformation("Theme initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize themes");
            throw;
        }
    }

    /// <summary>
    /// No cleanup required on shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
