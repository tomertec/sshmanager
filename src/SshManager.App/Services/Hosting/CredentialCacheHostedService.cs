using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SshManager.Data.Repositories;
using SshManager.Security;

namespace SshManager.App.Services.Hosting;

/// <summary>
/// Hosted service that manages the credential cache lifecycle and session state monitoring.
/// </summary>
public class CredentialCacheHostedService : IHostedService
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ICredentialCache _credentialCache;
    private readonly ISessionStateService _sessionStateService;
    private readonly ILogger<CredentialCacheHostedService> _logger;

    public CredentialCacheHostedService(
        ISettingsRepository settingsRepo,
        ICredentialCache credentialCache,
        ISessionStateService sessionStateService,
        ILogger<CredentialCacheHostedService> logger)
    {
        _settingsRepo = settingsRepo;
        _credentialCache = credentialCache;
        _sessionStateService = sessionStateService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the credential cache with settings and sets up session state monitoring.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing credential cache...");

        try
        {
            var settings = await _settingsRepo.GetAsync(cancellationToken);

            // Set timeout from settings
            if (settings.CredentialCacheTimeoutMinutes > 0)
            {
                _credentialCache.SetTimeout(TimeSpan.FromMinutes(settings.CredentialCacheTimeoutMinutes));
                _logger.LogDebug("Credential cache timeout set to {Timeout} minutes", settings.CredentialCacheTimeoutMinutes);
            }

            // Enable caching based on settings - this also starts the cleanup timer
            _credentialCache.EnableCaching(settings.EnableCredentialCaching);

            // Set up session state monitoring for clearing cache on lock
            if (settings.ClearCacheOnLock)
            {
                _sessionStateService.SessionLocked += OnSessionLocked;
                _sessionStateService.StartMonitoring();
                _logger.LogDebug("Session state monitoring started for credential cache clearing");
            }

            _logger.LogInformation("Credential caching initialized (enabled: {Enabled})", settings.EnableCredentialCaching);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize credential cache");
            throw;
        }
    }

    /// <summary>
    /// Clears the credential cache on exit if configured, stops monitoring, and disposes resources.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping credential cache service...");

        try
        {
            // Clear credential cache on exit if configured
            var settings = await _settingsRepo.GetAsync(cancellationToken);
            if (settings.ClearCacheOnExit)
            {
                _credentialCache.ClearAll();
                _logger.LogDebug("Credential cache cleared on exit");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear credential cache on exit");
        }

        try
        {
            // Stop session state monitoring
            _sessionStateService.SessionLocked -= OnSessionLocked;
            _sessionStateService.Dispose();
            _logger.LogDebug("Session state service disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing session state service");
        }

        try
        {
            // Dispose credential cache (securely clears all cached credentials)
            _credentialCache.Dispose();
            _logger.LogDebug("Credential cache disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing credential cache");
        }
    }

    /// <summary>
    /// Handles Windows session lock event by clearing the credential cache.
    /// </summary>
    private void OnSessionLocked(object? sender, EventArgs e)
    {
        _logger.LogInformation("Windows session locked - clearing credential cache");
        _credentialCache.ClearAll();
    }
}
