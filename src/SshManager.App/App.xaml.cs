using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SshManager.App.Infrastructure;
using SshManager.App.Services;
using SshManager.App.Views.Windows;
using SshManager.Data;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using Velopack;

namespace SshManager.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // Configure Serilog early for bootstrap logging
        Bootstrapper.ConfigureSerilog();

        // Set up global exception handlers before anything else
        Bootstrapper.SetupGlobalExceptionHandlers(this);

        // VELOPACK: Initialize Velopack before any other initialization
        // This handles first install hooks, updates, and uninstall cleanup
        try
        {
            VelopackApp.Build()
                .WithFirstRun(v => OnFirstRun(v.ToString()))
                .WithRestarted(v => OnAppRestarted(v.ToString()))
                .Run();
        }
        catch (Exception ex)
        {
            // Log but don't fail app startup if Velopack fails (e.g., dev environment)
            Log.Warning(ex, "Velopack initialization failed - this is normal in development");
        }

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddSshManagerServices();
            })
            .Build();

        Log.Information("SshManager application initialized");
    }

    /// <summary>
    /// Called on the first run after installation.
    /// Use this for any one-time setup tasks.
    /// </summary>
    private static void OnFirstRun(string version)
    {
        Log.Information("First run after installation: v{Version}", version);

        // TODO: Add any first-run setup here, such as:
        // - Show welcome dialog
        // - Create desktop shortcut
        // - Register file associations
        // - Import settings from previous version
    }

    /// <summary>
    /// Called after the app has been restarted following an update.
    /// </summary>
    private static void OnAppRestarted(string version)
    {
        Log.Information("Application restarted after update to v{Version}", version);

        // TODO: Add any post-update tasks here, such as:
        // - Show "What's New" dialog
        // - Run database migrations
        // - Clean up old files
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var logger = Log.ForContext<App>();
        logger.Information("Application starting up");

        try
        {
            await _host.StartAsync();
            logger.Debug("Host started successfully");

            // Ensure database is created
            var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            logger.Debug("Database initialized at {DbPath}", DbPaths.GetDbPath());

            // Apply schema migrations for new columns
            await DbMigrator.MigrateAsync(db, logger);

            // Initialize settings (creates defaults if not exist)
            var settingsRepo = _host.Services.GetRequiredService<ISettingsRepository>();
            await settingsRepo.GetAsync();
            logger.Debug("Application settings loaded");

            // Load custom terminal themes
            var themeService = _host.Services.GetRequiredService<ITerminalThemeService>();
            await themeService.LoadCustomThemesAsync();
            logger.Debug("Custom terminal themes loaded");

            // Seed sample host if database is empty
            var hostRepo = _host.Services.GetRequiredService<IHostRepository>();
            var hosts = await hostRepo.GetAllAsync();
            if (hosts.Count == 0)
            {
                await hostRepo.AddAsync(new Core.Models.HostEntry
                {
                    DisplayName = "Sample Host",
                    Hostname = "localhost",
                    Username = Environment.UserName,
                    Port = 22,
                    AuthType = Core.Models.AuthType.SshAgent,
                    Notes = "This is a sample host. Edit or delete it."
                });
                logger.Information("Created sample host entry for first-time user");
            }

            // Initialize system tray
            var trayService = _host.Services.GetRequiredService<ISystemTrayService>();
            trayService.Initialize();
            logger.Debug("System tray service initialized");

            // Initialize credential cache with settings
            await InitializeCredentialCacheAsync(logger);

            // Show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            logger.Information("Main window displayed, startup complete");
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error during application startup");
            MessageBox.Show(
                $"Failed to start the application:\n\n{ex.Message}\n\nCheck logs at %LocalAppData%\\SshManager\\logs",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var logger = Log.ForContext<App>();
        logger.Information("Application shutting down");

        try
        {
            // Clear credential cache on exit if configured
            await ClearCredentialCacheOnExitAsync(logger);

            // Clean up remote file editor temp files
            var remoteFileEditor = _host.Services.GetRequiredService<IRemoteFileEditorService>();
            await remoteFileEditor.CleanupAllAsync();
            logger.Debug("Remote file editor cleaned up");

            // Stop session state monitoring
            var sessionStateService = _host.Services.GetRequiredService<ISessionStateService>();
            sessionStateService.Dispose();
            logger.Debug("Session state service disposed");

            // Dispose system tray
            var trayService = _host.Services.GetRequiredService<ISystemTrayService>();
            trayService.Dispose();
            logger.Debug("System tray disposed");

            // Close all terminal sessions
            var sessionManager = _host.Services.GetRequiredService<ITerminalSessionManager>();
            await sessionManager.CloseAllSessionsAsync();
            logger.Debug("All terminal sessions closed");

            // Dispose credential cache (securely clears all cached credentials)
            var credentialCache = _host.Services.GetRequiredService<ICredentialCache>();
            credentialCache.Dispose();
            logger.Debug("Credential cache disposed");

            await _host.StopAsync();
            _host.Dispose();
            logger.Information("Host stopped and disposed");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during application shutdown");
        }
        finally
        {
            Log.Information("Application exit complete");
            await Log.CloseAndFlushAsync();
        }

        base.OnExit(e);
    }

    public static T GetService<T>() where T : class
    {
        if (Current == null)
            throw new InvalidOperationException($"Cannot get service {typeof(T).Name}: Application.Current is null");

        var app = Current as App;
        if (app == null)
            throw new InvalidOperationException($"Cannot get service {typeof(T).Name}: Application.Current is not App type");

        if (app._host == null)
            throw new InvalidOperationException($"Cannot get service {typeof(T).Name}: Host is not initialized");

        return app._host.Services.GetRequiredService<T>();
    }

    public static T? TryGetService<T>() where T : class
    {
        if (Current == null)
            return null;

        var app = Current as App;
        if (app == null)
            return null;

        if (app._host == null)
            return null;

        return app._host.Services.GetService<T>();
    }

    public static Microsoft.Extensions.Logging.ILogger<T> GetLogger<T>()
    {
        if (Current == null)
            throw new InvalidOperationException($"Cannot get logger {typeof(T).Name}: Application.Current is null");

        var app = Current as App;
        if (app == null)
            throw new InvalidOperationException($"Cannot get logger {typeof(T).Name}: Application.Current is not App type");

        if (app._host == null)
            throw new InvalidOperationException($"Cannot get logger {typeof(T).Name}: Host is not initialized");

        var loggerFactory = app._host.Services.GetRequiredService<ILoggerFactory>();
        return loggerFactory.CreateLogger<T>();
    }

    /// <summary>
    /// Initializes the credential cache with settings and sets up session state monitoring.
    /// </summary>
    private async Task InitializeCredentialCacheAsync(Serilog.ILogger logger)
    {
        var settingsRepo = _host.Services.GetRequiredService<ISettingsRepository>();
        var settings = await settingsRepo.GetAsync();

        var credentialCache = _host.Services.GetRequiredService<ICredentialCache>();

        // Set timeout from settings
        if (settings.CredentialCacheTimeoutMinutes > 0)
        {
            credentialCache.SetTimeout(TimeSpan.FromMinutes(settings.CredentialCacheTimeoutMinutes));
            logger.Debug("Credential cache timeout set to {Timeout} minutes", settings.CredentialCacheTimeoutMinutes);
        }

        // Set up session state monitoring for clearing cache on lock
        if (settings.ClearCacheOnLock)
        {
            var sessionStateService = _host.Services.GetRequiredService<ISessionStateService>();
            sessionStateService.SessionLocked += (s, e) =>
            {
                logger.Information("Windows session locked - clearing credential cache");
                credentialCache.ClearAll();
            };
            sessionStateService.StartMonitoring();
            logger.Debug("Session state monitoring started for credential cache clearing");
        }

        logger.Information("Credential caching initialized (enabled: {Enabled})", settings.EnableCredentialCaching);
    }

    /// <summary>
    /// Clears the credential cache on application exit if configured.
    /// </summary>
    private async Task ClearCredentialCacheOnExitAsync(Serilog.ILogger logger)
    {
        try
        {
            var settingsRepo = _host.Services.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepo.GetAsync();

            if (settings.ClearCacheOnExit)
            {
                var credentialCache = _host.Services.GetRequiredService<ICredentialCache>();
                credentialCache.ClearAll();
                logger.Debug("Credential cache cleared on exit");
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to clear credential cache on exit");
        }
    }
}
