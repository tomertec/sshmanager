using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SshManager.Data;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Handles database schema migrations for new columns and tables.
/// </summary>
public static class DbMigrator
{
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
    public static async Task MigrateAsync(AppDbContext db, Serilog.ILogger logger)
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
            ["ScrollbackBufferSize"] = ("INTEGER", AppConstants.MigrationDefaults.ScrollbackBufferSize.ToString()),
            // Session logging settings
            ["EnableSessionLogging"] = ("INTEGER", AppConstants.MigrationDefaults.EnableSessionLogging.ToString()),
            ["SessionLogDirectory"] = ("TEXT", "''"),
            ["SessionLogTimestampLines"] = ("INTEGER", AppConstants.MigrationDefaults.SessionLogTimestampLines.ToString()),
            ["MaxLogFileSizeMB"] = ("INTEGER", AppConstants.MigrationDefaults.MaxLogFileSizeMB.ToString()),
            ["MaxLogFilesToKeep"] = ("INTEGER", AppConstants.MigrationDefaults.MaxLogFilesToKeep.ToString()),
            ["SessionLogLevel"] = ("TEXT", "'OutputAndEvents'"),
            ["RedactTypedSecrets"] = ("INTEGER", "0"),
            ["MaxHistoryEntries"] = ("INTEGER", AppConstants.MigrationDefaults.MaxHistoryEntries.ToString()),
            ["HistoryRetentionDays"] = ("INTEGER", AppConstants.MigrationDefaults.HistoryRetentionDays.ToString()),
            ["ConnectionHistoryRetentionDays"] = ("INTEGER", AppConstants.MigrationDefaults.ConnectionHistoryRetentionDays.ToString()),
            ["WindowX"] = ("INTEGER", "NULL"),
            ["WindowY"] = ("INTEGER", "NULL"),
            ["WindowWidth"] = ("INTEGER", "NULL"),
            ["WindowHeight"] = ("INTEGER", "NULL"),
            // Credential caching settings
            ["EnableCredentialCaching"] = ("INTEGER", AppConstants.MigrationDefaults.EnableCredentialCaching.ToString()),
            ["CredentialCacheTimeoutMinutes"] = ("INTEGER", AppConstants.MigrationDefaults.CredentialCacheTimeoutMinutes.ToString()),
            ["ClearCacheOnLock"] = ("INTEGER", AppConstants.MigrationDefaults.ClearCacheOnLock.ToString()),
            ["ClearCacheOnExit"] = ("INTEGER", AppConstants.MigrationDefaults.ClearCacheOnExit.ToString()),
            // Backup settings
            ["EnableAutoBackup"] = ("INTEGER", AppConstants.MigrationDefaults.EnableAutoBackup.ToString()),
            ["BackupIntervalMinutes"] = ("INTEGER", AppConstants.MigrationDefaults.BackupIntervalMinutes.ToString()),
            ["MaxBackupCount"] = ("INTEGER", AppConstants.MigrationDefaults.MaxBackupCount.ToString()),
            ["BackupDirectory"] = ("TEXT", "NULL"),
            ["LastAutoBackupTime"] = ("TEXT", "NULL"),
            // Cloud sync settings
            ["EnableCloudSync"] = ("INTEGER", AppConstants.MigrationDefaults.EnableCloudSync.ToString()),
            ["SyncFolderPath"] = ("TEXT", "NULL"),
            ["SyncDeviceId"] = ("TEXT", "NULL"),
            ["SyncDeviceName"] = ("TEXT", "NULL"),
            ["LastSyncTime"] = ("TEXT", "NULL"),
            ["SyncIntervalMinutes"] = ("INTEGER", AppConstants.MigrationDefaults.SyncIntervalMinutes.ToString()),
            // Find in Terminal settings
            ["EnableFindInTerminal"] = ("INTEGER", AppConstants.MigrationDefaults.EnableFindInTerminal.ToString()),
            ["FindCaseSensitiveDefault"] = ("INTEGER", AppConstants.MigrationDefaults.FindCaseSensitiveDefault.ToString()),
            // Split pane settings
            ["EnableSplitPanes"] = ("INTEGER", AppConstants.MigrationDefaults.EnableSplitPanes.ToString()),
            ["ShowPaneHeaders"] = ("INTEGER", AppConstants.MigrationDefaults.ShowPaneHeaders.ToString()),
            ["DefaultSplitOrientation"] = ("TEXT", "'Vertical'"),
            ["MinimumPaneSize"] = ("INTEGER", AppConstants.MigrationDefaults.MinimumPaneSize.ToString()),
            // Terminal theme settings
            ["TerminalThemeId"] = ("TEXT", "'default'"),
            // Performance settings
            ["EnableHostListAnimations"] = ("INTEGER", AppConstants.MigrationDefaults.EnableHostListAnimations.ToString()),
            ["TerminalBufferInMemoryLines"] = ("INTEGER", AppConstants.MigrationDefaults.TerminalBufferInMemoryLines.ToString()),
            // SFTP browser settings
            ["SftpFavorites"] = ("TEXT", "''"),
            ["SftpMirrorNavigation"] = ("INTEGER", AppConstants.MigrationDefaults.SftpMirrorNavigation.ToString()),
            // Snippet Manager settings
            ["SnippetManagerOpacity"] = ("REAL", AppConstants.MigrationDefaults.SnippetManagerOpacity.ToString()),
            // Session recovery settings
            ["EnableSessionRecovery"] = ("INTEGER", AppConstants.MigrationDefaults.EnableSessionRecovery.ToString()),
            // Kerberos/GSSAPI settings
            ["EnableKerberosAuth"] = ("INTEGER", AppConstants.MigrationDefaults.EnableKerberosAuth.ToString()),
            ["DefaultKerberosDelegation"] = ("INTEGER", AppConstants.MigrationDefaults.DefaultKerberosDelegation.ToString()),
            // Connection pooling settings
            ["EnableConnectionPooling"] = ("INTEGER", AppConstants.MigrationDefaults.EnableConnectionPooling.ToString()),
            ["ConnectionPoolMaxPerHost"] = ("INTEGER", AppConstants.MigrationDefaults.ConnectionPoolMaxPerHost.ToString()),
            ["ConnectionPoolIdleTimeoutSeconds"] = ("INTEGER", AppConstants.MigrationDefaults.ConnectionPoolIdleTimeoutSeconds.ToString()),
            // X11 forwarding settings
            ["DefaultX11ForwardingEnabled"] = ("INTEGER", AppConstants.MigrationDefaults.DefaultX11ForwardingEnabled.ToString()),
            ["X11ServerPath"] = ("TEXT", "NULL"),
            ["AutoLaunchXServer"] = ("INTEGER", AppConstants.MigrationDefaults.AutoLaunchXServer.ToString()),
            // Autocompletion settings
            ["EnableAutocompletion"] = ("INTEGER", AppConstants.MigrationDefaults.EnableAutocompletion.ToString()),
            ["AutocompletionMode"] = ("INTEGER", AppConstants.MigrationDefaults.AutocompletionMode.ToString()),
            ["AutocompletionDebounceMs"] = ("INTEGER", AppConstants.MigrationDefaults.AutocompletionDebounceMs.ToString()),
            ["MaxCompletionSuggestions"] = ("INTEGER", AppConstants.MigrationDefaults.MaxCompletionSuggestions.ToString()),
            // Host list settings
            ["HostListViewMode"] = ("INTEGER", AppConstants.MigrationDefaults.HostListViewMode.ToString()),  // Normal = 1
            ["ShowHostConnectionStats"] = ("INTEGER", AppConstants.MigrationDefaults.ShowHostConnectionStats.ToString()),
            ["PinFavoritesToTop"] = ("INTEGER", AppConstants.MigrationDefaults.PinFavoritesToTop.ToString()),
            // Performance settings (Phase 1)
            ["TerminalOutputFlushIntervalMs"] = ("INTEGER", AppConstants.MigrationDefaults.TerminalOutputFlushIntervalMs.ToString()),
            ["TerminalOutputMaxBatchSize"] = ("INTEGER", AppConstants.MigrationDefaults.TerminalOutputMaxBatchSize.ToString()),
            ["EnableTerminalOutputBatching"] = ("INTEGER", AppConstants.MigrationDefaults.EnableTerminalOutputBatching.ToString()),
            // Connection settings (Phase 1)
            ["ReconnectBaseDelayMs"] = ("INTEGER", AppConstants.MigrationDefaults.ReconnectBaseDelayMs.ToString()),
            ["ReconnectMaxDelayMs"] = ("INTEGER", AppConstants.MigrationDefaults.ReconnectMaxDelayMs.ToString()),
            ["EnableNetworkMonitoring"] = ("INTEGER", AppConstants.MigrationDefaults.EnableNetworkMonitoring.ToString()),
            // Window layout settings
            ["LeftPanelWidth"] = ("REAL", "NULL"),
        };

        // Add missing settings columns using validated compile-time constants
        await AddMissingSettingsColumnsAsync(db, existingColumns, migrations, logger);

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
                $"ALTER TABLE Groups ADD COLUMN StatusCheckIntervalSeconds INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.StatusCheckIntervalSeconds}");
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
    /// Adds missing columns to the Settings table from a dictionary of migrations.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="existingColumns">Set of existing column names in the Settings table.</param>
    /// <param name="migrations">Dictionary of column migrations (name -> (type, defaultValue)).</param>
    /// <param name="logger">Logger for recording migration operations.</param>
    /// <remarks>
    /// <para><strong>SECURITY NOTE - SQL String Interpolation:</strong></para>
    /// <para>
    /// This method uses string interpolation to build SQL statements, which is normally a critical security
    /// vulnerability that enables SQL injection attacks. However, this specific usage is SAFE because:
    /// </para>
    /// <list type="number">
    /// <item>
    ///   <term>All values are compile-time constants:</term>
    ///   <description>
    ///   Column names, types, and default values come from the migrations dictionary declared inline
    ///   in <see cref="MigrateAsync"/>. They are hardcoded string literals, not user input.
    ///   </description>
    /// </item>
    /// <item>
    ///   <term>Strict validation via whitelists:</term>
    ///   <description>
    ///   Every value is validated before SQL construction:
    ///   <list type="bullet">
    ///     <item><see cref="IsValidColumnName"/>: Ensures column names contain only alphanumeric characters and underscores</item>
    ///     <item><see cref="AllowedColumnTypes"/>: Validates types against a whitelist (TEXT, INTEGER, REAL, BLOB only)</item>
    ///     <item><see cref="IsValidDefaultValue"/>: Ensures defaults are integers, NULL, or safely-quoted strings without embedded quotes</item>
    ///   </list>
    ///   </description>
    /// </item>
    /// <item>
    ///   <term>No user-provided data:</term>
    ///   <description>
    ///   This method is called only during application startup for schema migrations. No external input
    ///   can reach the SQL construction logic.
    ///   </description>
    /// </item>
    /// </list>
    /// <para><strong>WARNING:</strong> DO NOT copy this pattern for user-provided input or runtime data.</para>
    /// <para>
    /// For any operation involving user input, always use parameterized queries (e.g., ExecuteSqlAsync with parameters).
    /// String interpolation in SQL is only acceptable when ALL inputs are:
    /// </para>
    /// <list type="bullet">
    ///   <item>Compile-time constants defined in source code</item>
    ///   <item>Validated against strict whitelists</item>
    ///   <item>Never derived from user input, files, network, or any external source</item>
    /// </list>
    /// </remarks>
    private static async Task AddMissingSettingsColumnsAsync(
        AppDbContext db,
        HashSet<string> existingColumns,
        Dictionary<string, (string Type, string Default)> migrations,
        Serilog.ILogger logger)
    {
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

                // SAFE: String interpolation here is acceptable because all values are:
                // 1. Compile-time constants from the migrations dictionary
                // 2. Validated against strict whitelists (IsValidColumnName, AllowedColumnTypes, IsValidDefaultValue)
                // 3. Never derived from user input or external sources
                // DO NOT replicate this pattern for user-provided data - always use parameterized queries.
                var sql = $"ALTER TABLE Settings ADD COLUMN {columnName} {type} DEFAULT {defaultValue}";
                await db.Database.ExecuteSqlRawAsync(sql);
                logger.Information("Added missing column {ColumnName} to Settings table", columnName);
            }
        }
    }

    /// <summary>
    /// Creates missing tables that were added after the database was initially created.
    /// </summary>
    private static async Task CreateMissingTablesAsync(
        AppDbContext db,
        DbConnection connection,
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
            // Composite unique index on (HostId, Algorithm) allows storing multiple key types
            // (RSA, ED25519, ECDSA) per host for proper key rotation and multi-algorithm support
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
                CREATE UNIQUE INDEX IX_HostFingerprints_HostId_Algorithm ON HostFingerprints(HostId, Algorithm);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table HostFingerprints");
        }
        else
        {
            // Migrate existing HostFingerprints table: change unique index from HostId to (HostId, Algorithm)
            // This allows storing multiple key algorithms per host (RSA, ED25519, ECDSA, etc.)
            await MigrateHostFingerprintsIndexAsync(db, connection, logger);
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
                    KeySize INTEGER NOT NULL DEFAULT 0,
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

        if (!hostsColumns.Contains("SecureNotesProtected"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN SecureNotesProtected TEXT DEFAULT NULL");
            logger.Information("Added missing column SecureNotesProtected to Hosts table");
        }

        if (!hostsColumns.Contains("CreatedAt"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT ''");
            logger.Information("Added missing column CreatedAt to Hosts table");
        }

        if (!hostsColumns.Contains("UpdatedAt"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT ''");
            logger.Information("Added missing column UpdatedAt to Hosts table");
        }

        // Connection type and shell type columns
        if (!hostsColumns.Contains("ConnectionType"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN ConnectionType INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.ConnectionTypeSsh}");
            logger.Information("Added missing column ConnectionType to Hosts table");
        }

        if (!hostsColumns.Contains("ShellType"))
        {
            // ShellType.Auto = 0 (default) - assumes POSIX-compliant shell for SSH connections
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN ShellType INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.ShellTypeAuto}");
            logger.Information("Added missing column ShellType to Hosts table");
        }

        // Serial port support columns
        if (!hostsColumns.Contains("SerialPortName"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN SerialPortName TEXT DEFAULT NULL");
            logger.Information("Added missing column SerialPortName to Hosts table");
        }

        if (!hostsColumns.Contains("SerialBaudRate"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialBaudRate INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialBaudRate}");
            logger.Information("Added missing column SerialBaudRate to Hosts table");
        }

        if (!hostsColumns.Contains("SerialDataBits"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialDataBits INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialDataBits}");
            logger.Information("Added missing column SerialDataBits to Hosts table");
        }

        if (!hostsColumns.Contains("SerialStopBits"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialStopBits INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialStopBits}");
            logger.Information("Added missing column SerialStopBits to Hosts table");
        }

        if (!hostsColumns.Contains("SerialParity"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialParity INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialParity}");
            logger.Information("Added missing column SerialParity to Hosts table");
        }

        if (!hostsColumns.Contains("SerialHandshake"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialHandshake INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialHandshake}");
            logger.Information("Added missing column SerialHandshake to Hosts table");
        }

        if (!hostsColumns.Contains("SerialDtrEnable"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialDtrEnable INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialDtrEnable}");
            logger.Information("Added missing column SerialDtrEnable to Hosts table");
        }

        if (!hostsColumns.Contains("SerialRtsEnable"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialRtsEnable INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialRtsEnable}");
            logger.Information("Added missing column SerialRtsEnable to Hosts table");
        }

        if (!hostsColumns.Contains("SerialLocalEcho"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialLocalEcho INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.SerialLocalEcho}");
            logger.Information("Added missing column SerialLocalEcho to Hosts table");
        }

        if (!hostsColumns.Contains("SerialLineEnding"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN SerialLineEnding TEXT NOT NULL DEFAULT '\r\n'");
            logger.Information("Added missing column SerialLineEnding to Hosts table");
        }

        // Keep-alive configuration column
        if (!hostsColumns.Contains("KeepAliveIntervalSeconds"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN KeepAliveIntervalSeconds INTEGER DEFAULT NULL");
            logger.Information("Added missing column KeepAliveIntervalSeconds to Hosts table");
        }

        // Kerberos/GSSAPI authentication columns
        if (!hostsColumns.Contains("KerberosServicePrincipal"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN KerberosServicePrincipal TEXT DEFAULT NULL");
            logger.Information("Added missing column KerberosServicePrincipal to Hosts table");
        }

        if (!hostsColumns.Contains("KerberosDelegateCredentials"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN KerberosDelegateCredentials INTEGER NOT NULL DEFAULT 0");
            logger.Information("Added missing column KerberosDelegateCredentials to Hosts table");
        }

        // X11 forwarding columns
        if (!hostsColumns.Contains("X11ForwardingEnabled"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN X11ForwardingEnabled INTEGER DEFAULT NULL");
            logger.Information("Added missing column X11ForwardingEnabled to Hosts table");
        }

        if (!hostsColumns.Contains("X11TrustedForwarding"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN X11TrustedForwarding INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.X11TrustedForwarding}");
            logger.Information("Added missing column X11TrustedForwarding to Hosts table");
        }

        if (!hostsColumns.Contains("X11DisplayNumber"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Hosts ADD COLUMN X11DisplayNumber INTEGER DEFAULT NULL");
            logger.Information("Added missing column X11DisplayNumber to Hosts table");
        }

        // Phase 2 UI/UX: IsFavorite column for host favorites
        if (!hostsColumns.Contains("IsFavorite"))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Hosts ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT {AppConstants.MigrationDefaults.IsFavorite}");
            logger.Information("Added missing column IsFavorite to Hosts table");
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

        // Create Tags table if it doesn't exist
        if (!existingTables.Contains("Tags"))
        {
            var sql = @"
                CREATE TABLE Tags (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Color TEXT,
                    CreatedAt TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IX_Tags_Name ON Tags(Name);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table Tags");
        }
        else
        {
            // Migrate existing Tags table: add CreatedAt column if missing
            var tagsColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Tags)";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tagsColumns.Add(reader.GetString(1));
                }
            }

            if (!tagsColumns.Contains("CreatedAt"))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE Tags ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT ''");
                logger.Information("Added missing column CreatedAt to Tags table");
            }
        }

        // Create HostEntryTag junction table for many-to-many relationship if it doesn't exist
        if (!existingTables.Contains("HostEntryTag"))
        {
            var sql = @"
                CREATE TABLE HostEntryTag (
                    HostsId TEXT NOT NULL,
                    TagsId TEXT NOT NULL,
                    PRIMARY KEY (HostsId, TagsId),
                    FOREIGN KEY (HostsId) REFERENCES Hosts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (TagsId) REFERENCES Tags(Id) ON DELETE CASCADE
                );
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table HostEntryTag");
        }

        // Create HostEnvironmentVariables table if it doesn't exist
        if (!existingTables.Contains("HostEnvironmentVariables"))
        {
            var sql = @"
                CREATE TABLE HostEnvironmentVariables (
                    Id TEXT NOT NULL PRIMARY KEY,
                    HostEntryId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Value TEXT NOT NULL DEFAULT '',
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    Description TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (HostEntryId) REFERENCES Hosts(Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IX_HostEnvironmentVariables_HostEntryId_Name ON HostEnvironmentVariables(HostEntryId, Name);
                CREATE INDEX IX_HostEnvironmentVariables_HostEntryId_SortOrder ON HostEnvironmentVariables(HostEntryId, SortOrder);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table HostEnvironmentVariables");
        }

        // Create SessionRecordings table if it doesn't exist
        if (!existingTables.Contains("SessionRecordings"))
        {
            var sql = @"
                CREATE TABLE SessionRecordings (
                    Id TEXT NOT NULL PRIMARY KEY,
                    HostId TEXT,
                    Title TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    TerminalWidth INTEGER NOT NULL DEFAULT 80,
                    TerminalHeight INTEGER NOT NULL DEFAULT 24,
                    StartedAt TEXT NOT NULL,
                    EndedAt TEXT,
                    Duration TEXT NOT NULL DEFAULT '00:00:00',
                    FileSizeBytes INTEGER NOT NULL DEFAULT 0,
                    EventCount INTEGER NOT NULL DEFAULT 0,
                    Description TEXT,
                    IsArchived INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (HostId) REFERENCES Hosts(Id) ON DELETE SET NULL
                );
                CREATE INDEX IX_SessionRecordings_StartedAt ON SessionRecordings(StartedAt);
                CREATE INDEX IX_SessionRecordings_HostId ON SessionRecordings(HostId);
                CREATE INDEX IX_SessionRecordings_IsArchived ON SessionRecordings(IsArchived);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table SessionRecordings");
        }

        // Create SavedSessions table if it doesn't exist (for crash recovery)
        if (!existingTables.Contains("SavedSessions"))
        {
            var sql = @"
                CREATE TABLE SavedSessions (
                    Id TEXT NOT NULL PRIMARY KEY,
                    HostEntryId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    SavedAt TEXT NOT NULL,
                    WasGracefulShutdown INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (HostEntryId) REFERENCES Hosts(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_SavedSessions_HostEntryId ON SavedSessions(HostEntryId);
                CREATE INDEX IX_SavedSessions_WasGracefulShutdown ON SavedSessions(WasGracefulShutdown);
            ";
            await db.Database.ExecuteSqlRawAsync(sql);
            logger.Information("Created missing table SavedSessions");
        }
    }

    /// <summary>
    /// Migrates HostFingerprints table from single-key-per-host to multi-algorithm support.
    /// Changes the unique index from HostId alone to (HostId, Algorithm) composite.
    /// </summary>
    private static async Task MigrateHostFingerprintsIndexAsync(
        AppDbContext db,
        DbConnection connection,
        Serilog.ILogger logger)
    {
        // Check if old unique index exists (IX_HostFingerprints_HostId)
        var hasOldIndex = false;
        var hasNewIndex = false;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='HostFingerprints'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var indexName = reader.GetString(0);
                if (indexName.Equals("IX_HostFingerprints_HostId", StringComparison.OrdinalIgnoreCase))
                {
                    hasOldIndex = true;
                }
                else if (indexName.Equals("IX_HostFingerprints_HostId_Algorithm", StringComparison.OrdinalIgnoreCase))
                {
                    hasNewIndex = true;
                }
            }
        }

        // If new index already exists, no migration needed
        if (hasNewIndex)
        {
            return;
        }

        // If old index exists, we need to migrate
        if (hasOldIndex)
        {
            logger.Information("Migrating HostFingerprints index to support multiple key algorithms per host");

            // SQLite doesn't support DROP INDEX IF EXISTS in older versions, so we use a transaction
            // Drop old index and create new composite index
            var migrationSql = @"
                DROP INDEX IX_HostFingerprints_HostId;
                CREATE UNIQUE INDEX IX_HostFingerprints_HostId_Algorithm ON HostFingerprints(HostId, Algorithm);
            ";

            await db.Database.ExecuteSqlRawAsync(migrationSql);
            logger.Information("Successfully migrated HostFingerprints index to composite (HostId, Algorithm)");
        }
        else
        {
            // No index exists at all (shouldn't happen, but create the correct one)
            logger.Warning("HostFingerprints table exists but has no unique index. Creating composite index.");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IX_HostFingerprints_HostId_Algorithm ON HostFingerprints(HostId, Algorithm)");
        }
    }
}
