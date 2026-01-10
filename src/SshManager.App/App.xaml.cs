using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SshManager.Data;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.App.Views.Windows;

namespace SshManager.App;

public partial class App : Application
{
    private readonly IHost _host;
    private static Serilog.ILogger? _bootstrapLogger;

    public App()
    {
        // Configure Serilog early for bootstrap logging
        ConfigureSerilog();

        // Set up global exception handlers before anything else
        SetupGlobalExceptionHandlers();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();

        Log.Information("SshManager application initialized");
    }

    private static void ConfigureSerilog()
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

    private void SetupGlobalExceptionHandlers()
    {
        // UI thread exceptions
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Background thread exceptions (from async void, thread pool, etc.)
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // Task scheduler exceptions (unobserved task exceptions)
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Log.Debug("Global exception handlers configured");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            var dbPath = DbPaths.GetDbPath();
            options.UseSqlite($"Data Source={dbPath}");
        });

        // Repositories
        services.AddSingleton<IHostRepository, HostRepository>();
        services.AddSingleton<IGroupRepository, GroupRepository>();
        services.AddSingleton<IHostProfileRepository, HostProfileRepository>();
        services.AddSingleton<IConnectionHistoryRepository, ConnectionHistoryRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ISnippetRepository, SnippetRepository>();
        services.AddSingleton<IHostFingerprintRepository, HostFingerprintRepository>();
        services.AddSingleton<IManagedKeyRepository, ManagedKeyRepository>();
        services.AddSingleton<IProxyJumpProfileRepository, ProxyJumpProfileRepository>();
        services.AddSingleton<IPortForwardingProfileRepository, PortForwardingProfileRepository>();

        // Security
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ICredentialCache, SecureCredentialCache>();
        services.AddSingleton<ISshKeyManager, SshKeyManagerService>();
        services.AddSingleton<IPassphraseEncryptionService, PassphraseEncryptionService>();

        // Session state monitoring
        services.AddSingleton<ISessionStateService, SessionStateService>();

        // Terminal services
        services.AddSingleton<ITerminalResizeService, TerminalResizeService>();
        services.AddSingleton<ISshConnectionService, SshConnectionService>();
        services.AddSingleton<ISftpService, SftpService>();
        services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();
        services.AddSingleton<ISessionLoggingService, SessionLoggingService>();
        services.AddSingleton<IBroadcastInputService, BroadcastInputService>();
        services.AddSingleton<IServerStatsService, ServerStatsService>();
        services.AddSingleton<IProxyJumpService, ProxyJumpService>();
        services.AddSingleton<IPortForwardingService, PortForwardingService>();
        services.AddSingleton<ITerminalThemeService, TerminalThemeService>();

        // App services
        services.AddSingleton<IEditorThemeService, EditorThemeService>();
        services.AddSingleton<IRemoteFileEditorService, RemoteFileEditorService>();
        services.AddSingleton<IExportImportService, ExportImportService>();
        services.AddSingleton<ISshConfigParser, SshConfigParser>();
        services.AddSingleton<IPuttySessionImporter, PuttySessionImporter>();
        services.AddSingleton<ISystemTrayService, SystemTrayService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IPaneLayoutManager, PaneLayoutManager>();

        // Cloud sync services
        services.AddSingleton<IOneDrivePathDetector, OneDrivePathDetector>();
        services.AddSingleton<ISyncConflictResolver, SyncConflictResolver>();
        services.AddSingleton<ICloudSyncService, CloudSyncService>();
        services.AddSingleton<CloudSyncHostedService>();

        // Host status monitoring
        services.AddSingleton<IHostStatusService, HostStatusService>();

        // Background services
        services.AddHostedService<AutoBackupHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<CloudSyncHostedService>());
        services.AddHostedService<HostStatusHostedService>();

        // ViewModels
        services.AddSingleton<HostManagementViewModel>();
        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<SessionLoggingViewModel>();
        services.AddSingleton<BroadcastInputViewModel>();
        services.AddSingleton<SftpLauncherViewModel>();
        services.AddSingleton<ImportExportViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<PortForwardingManagerViewModel>();
        services.AddTransient<ProxyJumpProfileDialogViewModel>();
        services.AddTransient<PortForwardingProfileDialogViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
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
            await ApplySchemaMigrationsAsync(db, logger);

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
            sessionManager.CloseAllSessions();
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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
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

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._host.Services.GetRequiredService<T>();
    }

    public static Microsoft.Extensions.Logging.ILogger<T> GetLogger<T>()
    {
        var app = (App)Current;
        var loggerFactory = app._host.Services.GetRequiredService<ILoggerFactory>();
        return loggerFactory.CreateLogger<T>();
    }

    /// <summary>
    /// Allowed SQLite column types for schema migrations.
    /// </summary>
    private static readonly HashSet<string> AllowedColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT", "INTEGER", "REAL", "BLOB"
    };

    /// <summary>
    /// Validates a default value for SQLite column definitions.
    /// Only allows: integers, quoted strings (without embedded quotes), or NULL.
    /// </summary>
    private static bool IsValidDefaultValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Allow NULL
        if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return true;

        // Allow integers
        if (int.TryParse(value, out _))
            return true;

        // Allow quoted strings (must start and end with single quote, no embedded quotes)
        if (value.StartsWith('\'') && value.EndsWith('\'') && value.Length >= 2)
        {
            var inner = value[1..^1];
            // Disallow embedded quotes to prevent SQL injection
            return !inner.Contains('\'');
        }

        return false;
    }

    /// <summary>
    /// Validates a column name for schema migrations.
    /// Column names must be alphanumeric with underscores only.
    /// </summary>
    private static bool IsValidColumnName(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return false;

        // Must start with letter or underscore
        if (!char.IsLetter(columnName[0]) && columnName[0] != '_')
            return false;

        // Must contain only letters, digits, or underscores
        return columnName.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// Applies schema migrations for new columns and tables that don't exist in older databases.
    /// EnsureCreatedAsync() only creates tables if they don't exist, it doesn't add new columns or tables
    /// to an existing database.
    /// </summary>
    /// <remarks>
    /// SECURITY WARNING: This method uses string interpolation to build SQL statements.
    /// This is ONLY safe because:
    /// 1. All column names are validated via <see cref="IsValidColumnName"/> (alphanumeric + underscore only)
    /// 2. All types are validated against <see cref="AllowedColumnTypes"/> whitelist
    /// 3. All default values are validated via <see cref="IsValidDefaultValue"/> (integers, NULL, or quoted strings without embedded quotes)
    ///
    /// DO NOT copy this pattern elsewhere without implementing equivalent validation.
    /// For user-provided data, always use parameterized queries.
    /// </remarks>
    private static async Task ApplySchemaMigrationsAsync(AppDbContext db, Serilog.ILogger logger)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        // First, create missing tables
        await CreateMissingTablesAsync(db, connection, logger);

        // Then handle column migrations for Settings table
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Settings)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingColumns.Add(reader.GetString(1)); // Column name is at index 1
            }
        }

        // Define migrations: column name -> (type, default value)
        // All values are validated before being used in SQL to prevent injection
        var migrations = new Dictionary<string, (string Type, string Default)>
        {
            // Terminal settings
            ["ScrollbackBufferSize"] = ("INTEGER", "10000"),
            // Session logging settings
            ["EnableSessionLogging"] = ("INTEGER", "0"),
            ["SessionLogDirectory"] = ("TEXT", "''"),
            ["SessionLogTimestampLines"] = ("INTEGER", "1"),
            ["MaxLogFileSizeMB"] = ("INTEGER", "50"),
            ["MaxLogFilesToKeep"] = ("INTEGER", "5"),
            ["SessionLogLevel"] = ("TEXT", "'OutputAndEvents'"),
            ["RedactTypedSecrets"] = ("INTEGER", "0"),
            ["MaxHistoryEntries"] = ("INTEGER", "100"),
            ["HistoryRetentionDays"] = ("INTEGER", "0"),
            ["WindowX"] = ("INTEGER", "NULL"),
            ["WindowY"] = ("INTEGER", "NULL"),
            ["WindowWidth"] = ("INTEGER", "NULL"),
            ["WindowHeight"] = ("INTEGER", "NULL"),
            // Credential caching settings
            ["EnableCredentialCaching"] = ("INTEGER", "0"),
            ["CredentialCacheTimeoutMinutes"] = ("INTEGER", "15"),
            ["ClearCacheOnLock"] = ("INTEGER", "1"),
            ["ClearCacheOnExit"] = ("INTEGER", "1"),
            // Backup settings
            ["EnableAutoBackup"] = ("INTEGER", "0"),
            ["BackupIntervalMinutes"] = ("INTEGER", "60"),
            ["MaxBackupCount"] = ("INTEGER", "10"),
            ["BackupDirectory"] = ("TEXT", "NULL"),
            ["LastAutoBackupTime"] = ("TEXT", "NULL"),
            // Cloud sync settings
            ["EnableCloudSync"] = ("INTEGER", "0"),
            ["SyncFolderPath"] = ("TEXT", "NULL"),
            ["SyncDeviceId"] = ("TEXT", "NULL"),
            ["SyncDeviceName"] = ("TEXT", "NULL"),
            ["LastSyncTime"] = ("TEXT", "NULL"),
            ["SyncIntervalMinutes"] = ("INTEGER", "5"),
            // Find in Terminal settings
            ["EnableFindInTerminal"] = ("INTEGER", "1"),
            ["FindCaseSensitiveDefault"] = ("INTEGER", "0"),
            // Split pane settings
            ["EnableSplitPanes"] = ("INTEGER", "1"),
            ["ShowPaneHeaders"] = ("INTEGER", "1"),
            ["DefaultSplitOrientation"] = ("TEXT", "'Vertical'"),
            ["MinimumPaneSize"] = ("INTEGER", "100"),
            // Terminal theme settings
            ["TerminalThemeId"] = ("TEXT", "'default'"),
        };

        foreach (var (columnName, (type, defaultValue)) in migrations)
        {
            if (!existingColumns.Contains(columnName))
            {
                // Validate inputs to prevent SQL injection
                if (!IsValidColumnName(columnName))
                {
                    logger.Error("Invalid column name in migration: {ColumnName}", columnName);
                    throw new InvalidOperationException($"Invalid column name in migration: {columnName}");
                }

                if (!AllowedColumnTypes.Contains(type))
                {
                    logger.Error("Invalid column type in migration: {Type}", type);
                    throw new InvalidOperationException($"Invalid column type in migration: {type}");
                }

                if (!IsValidDefaultValue(defaultValue))
                {
                    logger.Error("Invalid default value in migration for column {ColumnName}: {DefaultValue}", columnName, defaultValue);
                    throw new InvalidOperationException($"Invalid default value in migration for column {columnName}: {defaultValue}");
                }

                var sql = $"ALTER TABLE Settings ADD COLUMN {columnName} {type} DEFAULT {defaultValue}";
                await db.Database.ExecuteSqlRawAsync(sql);
                logger.Information("Added missing column {ColumnName} to Settings table", columnName);
            }
        }

        // Migrate Groups table: add StatusCheckIntervalSeconds column if missing
        var groupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Groups)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                groupColumns.Add(reader.GetString(1));
            }
        }

        if (!groupColumns.Contains("StatusCheckIntervalSeconds"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Groups ADD COLUMN StatusCheckIntervalSeconds INTEGER NOT NULL DEFAULT 30");
            logger.Information("Added missing column StatusCheckIntervalSeconds to Groups table");
        }

        if (!groupColumns.Contains("Color"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Groups ADD COLUMN Color TEXT DEFAULT NULL");
            logger.Information("Added missing column Color to Groups table");
        }
    }

    /// <summary>
    /// Creates missing tables that were added after the database was initially created.
    /// </summary>
    private static async Task CreateMissingTablesAsync(
        AppDbContext db,
        System.Data.Common.DbConnection connection,
        Serilog.ILogger logger)
    {
        // Get existing tables
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingTables.Add(reader.GetString(0));
            }
        }

        // Create HostFingerprints table if it doesn't exist
        if (!existingTables.Contains("HostFingerprints"))
        {
            var sql = @"
                CREATE TABLE HostFingerprints (
                    Id TEXT NOT NULL PRIMARY KEY,
                    HostId TEXT NOT NULL,
                    Algorithm TEXT NOT NULL,
                    Fingerprint TEXT NOT NULL,
                    IsTrusted INTEGER NOT NULL DEFAULT 1,
                    FirstSeen TEXT NOT NULL,
                    LastSeen TEXT NOT NULL,
                    FOREIGN KEY (HostId) REFERENCES Hosts(Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IX_HostFingerprints_HostId ON HostFingerprints(HostId);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table HostFingerprints");
        }

        // Create ManagedSshKeys table if it doesn't exist
        if (!existingTables.Contains("ManagedSshKeys"))
        {
            var sql = @"
                CREATE TABLE ManagedSshKeys (
                    Id TEXT NOT NULL PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    PrivateKeyPath TEXT NOT NULL,
                    KeyType INTEGER NOT NULL,
                    Fingerprint TEXT NOT NULL,
                    Comment TEXT,
                    IsEncrypted INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    LastUsedAt TEXT
                );
                CREATE UNIQUE INDEX IX_ManagedSshKeys_PrivateKeyPath ON ManagedSshKeys(PrivateKeyPath);
                CREATE INDEX IX_ManagedSshKeys_DisplayName ON ManagedSshKeys(DisplayName);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table ManagedSshKeys");
        }
        else
        {
            // Migrate existing ManagedSshKeys table: add IsEncrypted column if missing
            var managedKeysColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(ManagedSshKeys)";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    managedKeysColumns.Add(reader.GetString(1));
                }
            }

            if (!managedKeysColumns.Contains("IsEncrypted"))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ManagedSshKeys ADD COLUMN IsEncrypted INTEGER NOT NULL DEFAULT 0");
                logger.Information("Added missing column IsEncrypted to ManagedSshKeys table");
            }
        }

        // Create Snippets table if it doesn't exist
        if (!existingTables.Contains("Snippets"))
        {
            var sql = @"
                CREATE TABLE Snippets (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Command TEXT NOT NULL,
                    Description TEXT,
                    Category TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_Snippets_Category ON Snippets(Category);
                CREATE INDEX IX_Snippets_SortOrder ON Snippets(SortOrder);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table Snippets");
        }

        // Create ProxyJumpProfiles table if it doesn't exist
        if (!existingTables.Contains("ProxyJumpProfiles"))
        {
            var sql = @"
                CREATE TABLE ProxyJumpProfiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    Description TEXT,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_ProxyJumpProfiles_DisplayName ON ProxyJumpProfiles(DisplayName);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table ProxyJumpProfiles");
        }

        // Create ProxyJumpHops table if it doesn't exist
        if (!existingTables.Contains("ProxyJumpHops"))
        {
            var sql = @"
                CREATE TABLE ProxyJumpHops (
                    Id TEXT NOT NULL PRIMARY KEY,
                    ProxyJumpProfileId TEXT NOT NULL,
                    JumpHostId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (ProxyJumpProfileId) REFERENCES ProxyJumpProfiles(Id) ON DELETE CASCADE,
                    FOREIGN KEY (JumpHostId) REFERENCES Hosts(Id) ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX IX_ProxyJumpHops_ProfileId_SortOrder ON ProxyJumpHops(ProxyJumpProfileId, SortOrder);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table ProxyJumpHops");
        }

        // Create PortForwardingProfiles table if it doesn't exist
        if (!existingTables.Contains("PortForwardingProfiles"))
        {
            var sql = @"
                CREATE TABLE PortForwardingProfiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    Description TEXT,
                    ForwardingType INTEGER NOT NULL DEFAULT 0,
                    LocalBindAddress TEXT NOT NULL DEFAULT '127.0.0.1',
                    LocalPort INTEGER NOT NULL,
                    RemoteHost TEXT,
                    RemotePort INTEGER,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    AutoStart INTEGER NOT NULL DEFAULT 0,
                    HostId TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (HostId) REFERENCES Hosts(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_PortForwardingProfiles_DisplayName ON PortForwardingProfiles(DisplayName);
                CREATE INDEX IX_PortForwardingProfiles_HostId ON PortForwardingProfiles(HostId);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table PortForwardingProfiles");
        }

        // Add ProxyJumpProfileId column to Hosts table if it doesn't exist
        var hostsColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Hosts)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                hostsColumns.Add(reader.GetString(1));
            }
        }

        if (!hostsColumns.Contains("ProxyJumpProfileId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN ProxyJumpProfileId TEXT REFERENCES ProxyJumpProfiles(Id) ON DELETE SET NULL");
            logger.Information("Added missing column ProxyJumpProfileId to Hosts table");
        }

        if (!hostsColumns.Contains("HostProfileId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN HostProfileId TEXT REFERENCES HostProfiles(Id) ON DELETE SET NULL");
            logger.Information("Added missing column HostProfileId to Hosts table");
        }

        if (!hostsColumns.Contains("SortOrder"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0");
            logger.Information("Added missing column SortOrder to Hosts table");
        }

        // Create HostProfiles table if it doesn't exist
        if (!existingTables.Contains("HostProfiles"))
        {
            var sql = @"
                CREATE TABLE HostProfiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    Description TEXT,
                    DefaultPort INTEGER NOT NULL DEFAULT 22,
                    DefaultUsername TEXT,
                    AuthType INTEGER NOT NULL DEFAULT 0,
                    PrivateKeyPath TEXT,
                    ProxyJumpProfileId TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (ProxyJumpProfileId) REFERENCES ProxyJumpProfiles(Id) ON DELETE SET NULL
                );
                CREATE INDEX IX_HostProfiles_DisplayName ON HostProfiles(DisplayName);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table HostProfiles");
        }
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
