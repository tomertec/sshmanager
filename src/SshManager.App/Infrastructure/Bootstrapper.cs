using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Serilog.Events;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Provides application bootstrapping functionality for logging and exception handling.
/// </summary>
public static class Bootstrapper
{
    private static Serilog.ILogger? _bootstrapLogger;

    /// <summary>
    /// Configures Serilog with file and debug output.
    /// </summary>
    public static void ConfigureSerilog()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SshManager",
            "logs");

        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "sshmanager-.log");

        // In Release builds, restrict file logging to Information level to prevent
        // debug logs (which may contain timing/diagnostic info) from being persisted.
#if DEBUG
        var fileMinimumLevel = LogEventLevel.Debug;
#else
        var fileMinimumLevel = LogEventLevel.Information;
#endif

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName()
            .WriteTo.Debug(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                restrictedToMinimumLevel: fileMinimumLevel,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                rollOnFileSizeLimit: true)
            .CreateLogger();

        _bootstrapLogger = Log.ForContext<App>();
        _bootstrapLogger.Information("Serilog configured. Log directory: {LogDirectory}", logDirectory);
    }

    /// <summary>
    /// Sets up global exception handlers for UI thread, background threads, and task scheduler.
    /// </summary>
    /// <param name="app">The WPF Application instance.</param>
    public static void SetupGlobalExceptionHandlers(Application app)
    {
        // UI thread exceptions
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Background thread exceptions (from async void, thread pool, etc.)
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Task scheduler exceptions (unobserved task exceptions)
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Log.Debug("Global exception handlers configured");
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI thread exception");

        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails have been logged.",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Fatal(exception, "Unhandled AppDomain exception. IsTerminating: {IsTerminating}", e.IsTerminating);

        if (e.IsTerminating)
        {
            MessageBox.Show(
                $"A fatal error occurred and the application must close:\n\n{exception?.Message}\n\nCheck logs at %LocalAppData%\\SshManager\\logs",
                "Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");

        // Mark as observed to prevent app termination
        e.SetObserved();
    }
}
