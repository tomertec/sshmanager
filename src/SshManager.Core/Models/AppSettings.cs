using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Application-wide settings persisted in the database.
/// </summary>
public sealed class AppSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ===== Terminal Settings =====

    /// <summary>
    /// Use embedded terminal (true) or launch external Windows Terminal (false).
    /// </summary>
    public bool UseEmbeddedTerminal { get; set; } = true;

    /// <summary>
    /// Font family for the terminal display.
    /// </summary>
    public string TerminalFontFamily { get; set; } = "Cascadia Mono";

    /// <summary>
    /// Font size for the terminal display.
    /// </summary>
    [Range(6, 72)]
    public int TerminalFontSize { get; set; } = 14;

    /// <summary>
    /// Maximum number of lines to retain in terminal scrollback history.
    /// </summary>
    [Range(100, 1000000)]
    public int ScrollbackBufferSize { get; set; } = 10000;

    /// <summary>
    /// Maximum number of lines to keep in memory for terminal output buffer.
    /// Older lines are compressed and archived to disk. Default: 5000 lines.
    /// </summary>
    public int TerminalBufferInMemoryLines { get; set; } = 5000;

    /// <summary>
    /// Enable the Find in Terminal feature (Ctrl+Shift+F).
    /// </summary>
    public bool EnableFindInTerminal { get; set; } = true;

    /// <summary>
    /// Default setting for case-sensitive search in Find in Terminal.
    /// </summary>
    public bool FindCaseSensitiveDefault { get; set; } = false;

    /// <summary>
    /// Selected terminal color theme ID.
    /// </summary>
    public string TerminalThemeId { get; set; } = "default";

    // ===== Connection Settings =====

    /// <summary>
    /// Default SSH port for new connections.
    /// </summary>
    public int DefaultPort { get; set; } = 22;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Keep-alive interval in seconds (0 to disable).
    /// </summary>
    [Range(0, 3600)]
    public int KeepAliveIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Auto-reconnect when connection is lost.
    /// </summary>
    public bool AutoReconnect { get; set; } = false;

    /// <summary>
    /// Maximum number of reconnection attempts.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    // ===== Security Settings =====

    /// <summary>
    /// Default path for SSH private keys.
    /// </summary>
    public string DefaultKeyPath { get; set; } = "";

    /// <summary>
    /// Preferred authentication method (SshAgent, PrivateKeyFile, Password).
    /// </summary>
    public string PreferredAuthMethod { get; set; } = "SshAgent";

    // ===== Credential Caching Settings =====

    /// <summary>
    /// Enable in-memory caching of credentials (passwords and key passphrases).
    /// </summary>
    public bool EnableCredentialCaching { get; set; } = false;

    /// <summary>
    /// Timeout in minutes for cached credentials (default: 15 minutes).
    /// </summary>
    public int CredentialCacheTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Clear cached credentials when Windows session is locked.
    /// </summary>
    public bool ClearCacheOnLock { get; set; } = true;

    /// <summary>
    /// Clear cached credentials when the application exits.
    /// </summary>
    public bool ClearCacheOnExit { get; set; } = true;

    // ===== Application Behavior =====

    /// <summary>
    /// Show confirmation dialog when closing sessions.
    /// </summary>
    public bool ConfirmOnClose { get; set; } = true;

    /// <summary>
    /// Remember window position and size between sessions.
    /// </summary>
    public bool RememberWindowPosition { get; set; } = true;

    /// <summary>
    /// Application theme: System, Light, or Dark.
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Start the application minimized.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Minimize to system tray instead of taskbar.
    /// </summary>
    public bool MinimizeToTray { get; set; } = false;

    // ===== Session Logging Settings =====

    /// <summary>
    /// Enable logging of terminal session output to files.
    /// </summary>
    public bool EnableSessionLogging { get; set; } = false;

    /// <summary>
    /// Directory path for session log files.
    /// </summary>
    public string SessionLogDirectory { get; set; } = "";

    /// <summary>
    /// Add timestamp prefix to each line in session logs.
    /// </summary>
    public bool SessionLogTimestampLines { get; set; } = true;

    /// <summary>
    /// Maximum size of each log file in megabytes before rotation.
    /// </summary>
    public int MaxLogFileSizeMB { get; set; } = 50;

    /// <summary>
    /// Maximum number of rotated log files to keep per session.
    /// </summary>
    public int MaxLogFilesToKeep { get; set; } = 5;

    /// <summary>
    /// Default session log level (OutputAndEvents, EventsOnly, ErrorsOnly).
    /// </summary>
    public string SessionLogLevel { get; set; } = "OutputAndEvents";

    /// <summary>
    /// Redact typed secrets from session logs.
    /// </summary>
    public bool RedactTypedSecrets { get; set; } = false;

    // ===== History Settings =====

    /// <summary>
    /// Maximum number of connection history entries to keep.
    /// </summary>
    public int MaxHistoryEntries { get; set; } = 100;

    /// <summary>
    /// Auto-delete history older than this many days (0 to disable).
    /// </summary>
    public int HistoryRetentionDays { get; set; } = 0;

    // ===== Window Position =====

    /// <summary>
    /// Saved window X position.
    /// </summary>
    public int? WindowX { get; set; }

    /// <summary>
    /// Saved window Y position.
    /// </summary>
    public int? WindowY { get; set; }

    /// <summary>
    /// Saved window width.
    /// </summary>
    public int? WindowWidth { get; set; }

    /// <summary>
    /// Saved window height.
    /// </summary>
    public int? WindowHeight { get; set; }

    // ===== Backup Settings =====

    /// <summary>
    /// Enable automatic backups of host configurations.
    /// </summary>
    public bool EnableAutoBackup { get; set; } = false;

    /// <summary>
    /// Interval between automatic backups in minutes.
    /// </summary>
    public int BackupIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum number of backup files to keep.
    /// </summary>
    public int MaxBackupCount { get; set; } = 10;

    /// <summary>
    /// Custom backup directory path (empty for default).
    /// </summary>
    public string? BackupDirectory { get; set; }

    /// <summary>
    /// Timestamp of the last automatic backup.
    /// </summary>
    public DateTimeOffset? LastAutoBackupTime { get; set; }

    // ===== Cloud Sync Settings =====

    /// <summary>
    /// Enable encrypted cloud synchronization via OneDrive.
    /// </summary>
    public bool EnableCloudSync { get; set; } = false;

    /// <summary>
    /// Path to the sync folder (typically OneDrive\SshManager).
    /// </summary>
    public string? SyncFolderPath { get; set; }

    /// <summary>
    /// Unique identifier for this device in sync operations.
    /// </summary>
    public string? SyncDeviceId { get; set; }

    /// <summary>
    /// Human-readable name for this device in sync operations.
    /// </summary>
    public string? SyncDeviceName { get; set; }

    /// <summary>
    /// Timestamp of the last successful sync operation.
    /// </summary>
    public DateTimeOffset? LastSyncTime { get; set; }

    /// <summary>
    /// Interval between automatic sync operations in minutes.
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 5;

    // ===== Split Pane Settings =====

    /// <summary>
    /// Enable split pane functionality.
    /// </summary>
    public bool EnableSplitPanes { get; set; } = true;

    /// <summary>
    /// Show headers on panes with session title and controls.
    /// </summary>
    public bool ShowPaneHeaders { get; set; } = true;

    /// <summary>
    /// Default split orientation when splitting panes (Vertical or Horizontal).
    /// </summary>
    public string DefaultSplitOrientation { get; set; } = "Vertical";

    /// <summary>
    /// Minimum pane size in pixels.
    /// </summary>
    public int MinimumPaneSize { get; set; } = 100;

    // ===== Performance Settings =====

    /// <summary>
    /// Enable animations for host list items (disable for better performance with 500+ hosts).
    /// </summary>
    public bool EnableHostListAnimations { get; set; } = true;

    // ===== SFTP Browser Settings =====

    /// <summary>
    /// Saved remote path favorites/bookmarks (JSON serialized list of paths).
    /// Format: "hostname:path" entries separated by "|"
    /// </summary>
    public string SftpFavorites { get; set; } = "";

    /// <summary>
    /// Enable mirror navigation mode in SFTP browser (sync local and remote navigation).
    /// </summary>
    public bool SftpMirrorNavigation { get; set; } = false;

    // ===== Snippet Manager Settings =====

    /// <summary>
    /// Window opacity for the Snippet Manager dialog (0.2 to 1.0).
    /// </summary>
    public double SnippetManagerOpacity { get; set; } = 1.0;
}
