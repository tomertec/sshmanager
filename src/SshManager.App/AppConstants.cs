namespace SshManager.App;

/// <summary>
/// Internal constants for the App module.
/// These values are used for UI defaults, database migrations, and application behavior.
/// </summary>
internal static class AppConstants
{
    /// <summary>
    /// Database migration default values.
    /// These are used in DbMigrator for schema migrations.
    /// </summary>
    public static class MigrationDefaults
    {
        // Terminal settings
        public const int ScrollbackBufferSize = 10000;

        // Session logging settings
        public const int EnableSessionLogging = 0;
        public const int SessionLogTimestampLines = 1;
        public const int MaxLogFileSizeMB = 50;
        public const int MaxLogFilesToKeep = 5;
        public const int MaxHistoryEntries = 100;
        public const int HistoryRetentionDays = 0;
        public const int ConnectionHistoryRetentionDays = 90;

        // Credential caching settings
        public const int EnableCredentialCaching = 0;
        public const int CredentialCacheTimeoutMinutes = 15;
        public const int ClearCacheOnLock = 1;
        public const int ClearCacheOnExit = 1;

        // Backup settings
        public const int EnableAutoBackup = 0;
        public const int BackupIntervalMinutes = 60;
        public const int MaxBackupCount = 10;

        // Cloud sync settings
        public const int EnableCloudSync = 0;
        public const int SyncIntervalMinutes = 5;

        // Find in Terminal settings
        public const int EnableFindInTerminal = 1;
        public const int FindCaseSensitiveDefault = 0;

        // Split pane settings
        public const int EnableSplitPanes = 1;
        public const int ShowPaneHeaders = 1;
        public const int MinimumPaneSize = 100;

        // Terminal theme settings
        public const string TerminalThemeId = "default";

        // Performance settings
        public const int EnableHostListAnimations = 1;
        public const int TerminalBufferInMemoryLines = 5000;

        // SFTP browser settings
        public const int SftpMirrorNavigation = 0;

        // Snippet Manager settings
        public const double SnippetManagerOpacity = 1.0;

        // Session recovery settings
        public const int EnableSessionRecovery = 1;

        // Kerberos/GSSAPI settings
        public const int EnableKerberosAuth = 0;
        public const int DefaultKerberosDelegation = 0;

        // Connection pooling settings
        public const int EnableConnectionPooling = 0;
        public const int ConnectionPoolMaxPerHost = 3;
        public const int ConnectionPoolIdleTimeoutSeconds = 300;

        // X11 forwarding settings
        public const int DefaultX11ForwardingEnabled = 0;
        public const int AutoLaunchXServer = 0;

        // Autocompletion settings
        public const int EnableAutocompletion = 0;
        public const int AutocompletionMode = 0;
        public const int AutocompletionDebounceMs = 150;
        public const int MaxCompletionSuggestions = 10;

        // Host list settings
        public const int HostListViewMode = 1;  // Normal = 1
        public const int ShowHostConnectionStats = 1;
        public const int PinFavoritesToTop = 1;

        // Performance settings (Phase 1)
        public const int TerminalOutputFlushIntervalMs = 16;
        public const int TerminalOutputMaxBatchSize = 8192;
        public const int EnableTerminalOutputBatching = 1;

        // Connection settings (Phase 1)
        public const int ReconnectBaseDelayMs = 1000;
        public const int ReconnectMaxDelayMs = 30000;
        public const int EnableNetworkMonitoring = 1;

        // Groups table
        public const int StatusCheckIntervalSeconds = 30;

        // Host table defaults
        public const int DefaultPort = 22;
        public const int SerialBaudRate = 9600;
        public const int SerialDataBits = 8;
        public const int SerialStopBits = 0;
        public const int SerialParity = 0;
        public const int SerialHandshake = 0;
        public const int SerialDtrEnable = 1;
        public const int SerialRtsEnable = 1;
        public const int SerialLocalEcho = 0;
        public const int ConnectionTypeSsh = 0;
        public const int ShellTypeAuto = 0;
        public const int X11TrustedForwarding = 0;
        public const int KerberosDelegateCredentials = 0;
        public const int IsFavorite = 0;
        public const int SortOrder = 0;

        // Port forwarding
        public const string LocalBindAddress = "127.0.0.1";
        public const int ForwardingTypeLocal = 0;
        public const int PortForwardingEnabled = 1;
        public const int PortForwardingAutoStart = 0;

        // Proxy jump profiles
        public const int ProxyJumpProfileEnabled = 1;

        // Host profiles
        public const int HostProfileDefaultPort = 22;
        public const int HostProfileAuthType = 0; // SshAgent

        // Managed SSH keys
        public const int ManagedSshKeyIsEncrypted = 0;

        // Host fingerprints
        public const int HostFingerprintIsTrusted = 1;

        // Session recordings
        public const int SessionRecordingWidth = 80;
        public const int SessionRecordingHeight = 24;
        public const int SessionRecordingIsArchived = 0;

        // Saved sessions
        public const int SavedSessionWasGracefulShutdown = 0;

        // Environment variables
        public const int EnvironmentVariableIsEnabled = 1;
        public const int EnvironmentVariableSortOrder = 0;
    }

    /// <summary>
    /// SFTP transfer UI defaults.
    /// </summary>
    public static class SftpDefaults
    {
        public const int AutoRemoveDelayMs = 5000;
        public const int DuplicateNameRetryLimit = 1000;
    }

    /// <summary>
    /// Tunnel builder UI defaults.
    /// </summary>
    public static class TunnelBuilderDefaults
    {
        public const int NodeGridColumns = 3;
        public const int NodeSpacingX = 200;
        public const int NodeSpacingY = 150;
        public const int NodeGridOffsetX = 50;
        public const int NodeGridOffsetY = 50;
        public const double NodeWidth = 120;
        public const double NodeHeight = 80;
    }

    /// <summary>
    /// Canvas zoom defaults.
    /// </summary>
    public static class CanvasDefaults
    {
        public const double ZoomMin = 0.25;
        public const double ZoomMax = 2.0;
        public const double ZoomStep = 0.1;
    }

    /// <summary>
    /// Delay and timing defaults.
    /// </summary>
    public static class DelayDefaults
    {
        public const int TextEditorAutoSaveDelayMs = 2000;
        public const int HostSearchDebounceMs = 300;
        public const int StatusCheckDelayMs = 1500;
        public const int AutoBackupStartupDelaySeconds = 30;
        public const int AutoBackupErrorDelayMinutes = 1;
        public const int AutoBackupFailureDelayMinutes = 5;
        public const int DialogTimeoutSeconds = 3;
        public const int DialogShortTimeoutSeconds = 2;
        public const int DialogLongTimeoutSeconds = 5;
    }

    /// <summary>
    /// Animation timing defaults (in milliseconds).
    /// </summary>
    public static class AnimationDefaults
    {
        public const int ShortDurationMs = 100;
        public const int MediumDurationMs = 150;
        public const int LongDurationMs = 200;
        public const int BeginTimeOffsetMs = 100;
    }

    /// <summary>
    /// Test server defaults.
    /// </summary>
    public static class TestDefaults
    {
        public const string DefaultPipeName = "SshManagerTestPipe";
        public const int PipeReadyTimeoutSeconds = 5;
        public const int CommandPollIntervalMs = 100;
    }

    /// <summary>
    /// Percentage and calculation constants.
    /// </summary>
    public static class PercentageDefaults
    {
        public const double ProgressMaxPercent = 100.0;
    }

    /// <summary>
    /// Shell icon service constants.
    /// </summary>
    public static class ShellIcons
    {
        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_SMALLICON = 0x1;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    }
}
