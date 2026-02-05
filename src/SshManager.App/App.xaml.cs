using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SshManager.App.Infrastructure;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.App.Views.Dialogs;
using SshManager.App.Views.Windows;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using Wpf.Ui.Appearance;
#if DEBUG
using SshManager.App.Services.Testing;
#endif

namespace SshManager.App;

public partial class App : Application
{
    private readonly IHost _host;
#if DEBUG
    private ITestServer? _testServer;
#endif

    public App()
    {
        // Configure Serilog early for bootstrap logging
        Bootstrapper.ConfigureSerilog();

        // Set up global exception handlers before anything else
        Bootstrapper.SetupGlobalExceptionHandlers(this);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddSshManagerServices();
            })
            .Build();

        Log.Information("SshManager application initialized");
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var logger = Log.ForContext<App>();
        logger.Information("Application starting up");

        // Clean up stale temp files from previous editor sessions
        TextEditorViewModel.CleanupStaleEditTempFiles();

        // Show startup splash window immediately to avoid frozen appearance
        var startupWindow = new StartupWindow();
        startupWindow.Show();
        startupWindow.UpdateStatus("Initializing...");

        try
        {
            // Start the host - this will execute all hosted services in order:
            // 1. DatabaseInitializationHostedService (database creation, migrations, seeding)
            // 2. ThemeInitializationHostedService (application and terminal themes)
            // 3. SystemTrayHostedService (system tray icon)
            // 4. CredentialCacheHostedService (credential cache and session monitoring)
            // 5. StartupTasksHostedService (connection history cleanup)
            startupWindow.UpdateStatus("Initializing database...");
            await _host.StartAsync();
            logger.Debug("Host started successfully - all startup services completed");

            // Load settings for session recovery check
            startupWindow.UpdateStatus("Loading settings...");
            var settingsRepo = _host.Services.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepo.GetAsync();

            // Show main window
            startupWindow.UpdateStatus("Preparing main window...");
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Set MainWindow explicitly so dialogs can use Application.Current.MainWindow as their owner.
            // WPF sets MainWindow to the first window shown (StartupWindow), so we must update it.
            MainWindow = mainWindow;
            logger.Information("Main window displayed");

            // Close the startup window now that main window is visible
            startupWindow.Close();

#if DEBUG
            // Start test automation server (DEBUG only)
            await StartTestServerAsync(logger);
#endif

            // Check for crash recovery sessions (after main window is shown)
            // This stays here because it requires the MainWindow reference
            if (settings.EnableSessionRecovery)
            {
                await CheckSessionRecoveryAsync(logger, mainWindow);
            }
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error during application startup");
            startupWindow.Close();
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
#if DEBUG
            // Stop test automation server
            await StopTestServerAsync(logger);
#endif

            // Save active sessions for crash recovery (marked as graceful)
            await SaveSessionsForRecoveryAsync(logger, gracefulShutdown: true);

            // Clean up remote file editor temp files
            var remoteFileEditor = _host.Services.GetRequiredService<IRemoteFileEditorService>();
            await remoteFileEditor.CleanupAllAsync();
            logger.Debug("Remote file editor cleaned up");

            // Close all terminal sessions
            var sessionManager = _host.Services.GetRequiredService<ITerminalSessionManager>();
            await sessionManager.CloseAllSessionsAsync();
            logger.Debug("All terminal sessions closed");

            // Stop the host - this will execute StopAsync on all hosted services in reverse order:
            // - CredentialCacheHostedService (clears cache if configured, disposes session state and cache)
            // - SystemTrayHostedService (disposes system tray)
            // - Other services...
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
    /// Applies the application theme using WPF-UI's ApplicationThemeManager.
    /// </summary>
    /// <param name="theme">Theme name: "Dark", "Light", or "System".</param>
    public static void ApplyApplicationTheme(string theme)
    {
        var appTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            "System" => ApplicationTheme.Unknown, // Unknown allows WPF-UI to follow system theme
            _ => ApplicationTheme.Dark
        };
        ApplicationThemeManager.Apply(appTheme);
    }

    /// <summary>
    /// Checks for recoverable sessions from a previous crash and offers to restore them.
    /// </summary>
    private async Task CheckSessionRecoveryAsync(Serilog.ILogger logger, Window ownerWindow)
    {
        try
        {
            var savedSessionRepo = _host.Services.GetRequiredService<ISavedSessionRepository>();
            var recoverableSessions = await savedSessionRepo.GetRecoverableSessionsAsync();

            if (recoverableSessions.Count == 0)
            {
                // No crash recovery needed - clear any gracefully closed sessions
                await savedSessionRepo.ClearAllAsync();
                return;
            }

            logger.Information("Found {Count} sessions to potentially recover", recoverableSessions.Count);

            // Show recovery dialog
            var viewModel = new SessionRecoveryViewModel(recoverableSessions);
            var dialog = new SessionRecoveryDialog(viewModel);
            dialog.Owner = ownerWindow;
            dialog.ShowDialog();

            if (viewModel.ShouldRestore)
            {
                logger.Information("User chose to restore {Count} sessions", recoverableSessions.Count);

                // Restore sessions by connecting to their hosts
                var hostRepo = _host.Services.GetRequiredService<IHostRepository>();
                var mainWindowViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();

                foreach (var savedSession in recoverableSessions)
                {
                    try
                    {
                        var host = await hostRepo.GetByIdAsync(savedSession.HostEntryId);
                        if (host != null)
                        {
                            await mainWindowViewModel.Session.ConnectCommand.ExecuteAsync(host);
                            logger.Debug("Restored session for host {HostName}", host.DisplayName);
                        }
                        else
                        {
                            logger.Warning("Could not restore session - host {HostId} not found", savedSession.HostEntryId);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "Failed to restore session for host {HostId}", savedSession.HostEntryId);
                    }
                }
            }
            else
            {
                logger.Information("User chose not to restore sessions");
            }

            // Clear saved sessions after recovery attempt
            await savedSessionRepo.ClearAllAsync();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to check for session recovery - continuing with startup");
        }
    }

    /// <summary>
    /// Saves active terminal sessions for crash recovery.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="gracefulShutdown">Whether this is a graceful shutdown.</param>
    private async Task SaveSessionsForRecoveryAsync(Serilog.ILogger logger, bool gracefulShutdown)
    {
        try
        {
            var settingsRepo = _host.Services.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepo.GetAsync();

            if (!settings.EnableSessionRecovery)
            {
                return;
            }

            var sessionManager = _host.Services.GetRequiredService<ITerminalSessionManager>();
            var savedSessionRepo = _host.Services.GetRequiredService<ISavedSessionRepository>();

            // Get active sessions
            var activeSessions = sessionManager.Sessions
                .Where(s => s.IsConnected)
                .ToList();

            if (activeSessions.Count == 0)
            {
                // No active sessions - clear any saved sessions
                await savedSessionRepo.ClearAllAsync();
                return;
            }

            // Save each session
            var savedSessions = activeSessions
                .Where(s => s.Host != null)
                .Select(s => new SavedSession
                {
                    Id = s.Id,
                    HostEntryId = s.Host!.Id,
                    Title = s.Title,
                    CreatedAt = s.CreatedAt,
                    SavedAt = DateTimeOffset.UtcNow,
                    WasGracefulShutdown = gracefulShutdown
                })
                .ToList();

            if (savedSessions.Count > 0)
            {
                // Clear existing and save new
                await savedSessionRepo.ClearAllAsync();
                await savedSessionRepo.SaveAllAsync(savedSessions);
                logger.Debug("Saved {Count} sessions for recovery (graceful: {Graceful})",
                    savedSessions.Count, gracefulShutdown);
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to save sessions for recovery");
        }
    }

#if DEBUG
    /// <summary>
    /// Starts the test automation server for external testing tools.
    /// Only available in DEBUG builds.
    /// </summary>
    private async Task StartTestServerAsync(Serilog.ILogger logger)
    {
        try
        {
            _testServer = _host.Services.GetService<ITestServer>();
            if (_testServer != null)
            {
                await _testServer.StartAsync();
                logger.Information("Test automation server started on pipe: {PipeName}", _testServer.PipeName);
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to start test automation server - continuing without it");
        }
    }

    /// <summary>
    /// Stops the test automation server.
    /// Only available in DEBUG builds.
    /// </summary>
    private async Task StopTestServerAsync(Serilog.ILogger logger)
    {
        try
        {
            if (_testServer != null)
            {
                await _testServer.StopAsync();
                _testServer.Dispose();
                logger.Debug("Test automation server stopped");
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Error stopping test automation server");
        }
    }
#endif
}
