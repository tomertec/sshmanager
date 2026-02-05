namespace SshManager.Terminal;

/// <summary>
/// Internal constants for the Terminal module.
/// These values are used for SSH connections, terminal display, and data transfer.
/// </summary>
internal static class TerminalConstants
{
    /// <summary>
    /// SSH connection defaults.
    /// </summary>
    public static class SshDefaults
    {
        public const int DefaultBufferSize = 4096;
        public const string DefaultTerminalName = "xterm-256color";
        public const int MinPort = 1;
        public const int MaxPort = 65535;
        public const int DefaultConnectionTimeoutSeconds = 30;
        public const int DefaultKeepAliveIntervalSeconds = 60;
        public const int HealthCheckIntervalSeconds = 10;
    }

    /// <summary>
    /// Reconnection and retry policy defaults.
    /// </summary>
    public static class ReconnectionDefaults
    {
        public const int InitialDelaySeconds = 1;
        public const int MaxDelaySeconds = 30;
        public const int InitialDelayMs = 500;
        public const int MaxDelayMs = 15000;
        public const int ReconnectDelaySeconds = 2;
    }

    /// <summary>
    /// Network monitoring defaults.
    /// </summary>
    public static class NetworkDefaults
    {
        public const int DefaultHostCheckTimeoutSeconds = 5;
        public const int KerberosCacheDurationSeconds = 30;
    }

    /// <summary>
    /// Terminal display and font defaults.
    /// </summary>
    public static class DisplayDefaults
    {
        public const double DefaultFontSize = 14.0;
        public const double MinFontSize = 8.0;
        public const double MaxFontSize = 32.0;
        public const double FontSizeStep = 1.0;
        public const string DefaultFontFamily = "Cascadia Mono";
        public const int MaxPreviewLength = 200;
        public const int FitDebounceMs = 100;
    }

    /// <summary>
    /// Terminal output buffer defaults.
    /// </summary>
    public static class BufferDefaults
    {
        public const int SegmentSize = 1000;
        public const int MaxPendingArchives = 10;
        public const int DefaultMaxLines = 10000;
        public const int DefaultMaxLinesInMemory = 5000;
        public const int MinLines = 100;
        public const int MaxLines = 100000;
        public const int ArchiveWorkerTimeoutSeconds = 5;
    }

    /// <summary>
    /// SFTP transfer defaults.
    /// </summary>
    public static class SftpDefaults
    {
        public const int TransferBufferSize = 81920;
        public const long MaxReadAllBytesSize = 50 * 1024 * 1024; // 50 MB
        public const int MaxUnixPermissions = 4095; // 07777 octal = 4095 decimal
    }

    /// <summary>
    /// Autocompletion service defaults.
    /// </summary>
    public static class AutocompletionDefaults
    {
        public const int MaxRemoteCompletions = 20;
        public const int MaxLocalCompletions = 15;
        public const int RemoteCompletionTimeoutMs = 1000;
    }

    /// <summary>
    /// Keyboard escape sequences.
    /// </summary>
    public static class KeySequences
    {
        public const char Escape = '\x1B';
        public const string DeleteKeySequence = "\x1b[3~";
        public const string InsertKeySequence = "\x1b[2~";
    }

    /// <summary>
    /// Serial port defaults.
    /// </summary>
    public static class SerialDefaults
    {
        public const int BreakSignalDurationMs = 250;
        public const int ReadTaskTimeoutSeconds = 2;
    }

    /// <summary>
    /// Recording and playback defaults.
    /// </summary>
    public static class RecordingDefaults
    {
        public const int FlushIntervalMs = 100;
        public const int FlushThreshold = 1000;
        public const int DefaultTerminalWidth = 80;
        public const int DefaultTerminalHeight = 24;
        public const int SeekPollIntervalMs = 100;
        public const int PlaybackPollIntervalMs = 100;
    }

    /// <summary>
    /// Tunnel and port forwarding defaults.
    /// </summary>
    public static class TunnelDefaults
    {
        public const string LocalBindAddress = "127.0.0.1";
        public const int DefaultTimeoutSeconds = 30;
        public const int ShortTimeoutSeconds = 2;
        public const int MediumTimeoutSeconds = 3;
        public const int LongTimeoutSeconds = 5;
    }

    /// <summary>
    /// Stats collection defaults.
    /// </summary>
    public static class StatsDefaults
    {
        public const int CollectionIntervalSeconds = 1;
        public const int ServerStatsTimeoutSeconds = 3;
    }

    /// <summary>
    /// Connection pool defaults.
    /// </summary>
    public static class ConnectionPoolDefaults
    {
        public const int CleanupIntervalSeconds = 30;
        public const int IdleTimeoutSeconds = 300;
        public const int MaxPerHost = 3;
    }

    /// <summary>
    /// Default theme color values (xterm.js dark theme).
    /// </summary>
    public static class ThemeColors
    {
        public const string Background = "#0C0C0C";
        public const string Foreground = "#CCCCCC";
        public const string Cursor = "#CCCCCC";
        public const string CursorAccent = "#0C0C0C";
        public const string SelectionBackground = "#3399FF";

        // Standard ANSI colors (0-7)
        public const string Black = "#0C0C0C";
        public const string Red = "#C50F1F";
        public const string Green = "#13A10E";
        public const string Yellow = "#C19C00";
        public const string Blue = "#0037DA";
        public const string Magenta = "#881798";
        public const string Cyan = "#3A96DD";
        public const string White = "#CCCCCC";

        // Bright ANSI colors (8-15)
        public const string BrightBlack = "#767676";
        public const string BrightRed = "#E74856";
        public const string BrightGreen = "#16C60C";
        public const string BrightYellow = "#F9F1A5";
        public const string BrightBlue = "#3B78FF";
        public const string BrightMagenta = "#B4009E";
        public const string BrightCyan = "#61D6D6";
        public const string BrightWhite = "#F2F2F2";

        // Fallback colors
        public const string FallbackColor = "#000000";
    }

    /// <summary>
    /// Bridge and connection timing defaults.
    /// These timeouts are used during bridge disposal and task cleanup.
    /// </summary>
    public static class BridgeDefaults
    {
        public const int ReadBufferSize = 8192;
        public const int WebMaxBatchSize = 8192;

        /// <summary>
        /// Timeout for waiting on the read task to complete during bridge disposal.
        /// The read task processes incoming SSH data and should respond quickly to cancellation.
        /// 2 seconds provides enough time for the read loop to exit without blocking shutdown.
        /// </summary>
        public const int ReadTaskDisposeTimeoutSeconds = 2;

        /// <summary>
        /// Timeout for waiting on the health check task to complete during bridge disposal.
        /// Health checks are simpler than read operations and should terminate faster.
        /// 1 second is sufficient as health check loops check cancellation frequently.
        /// </summary>
        public const int HealthCheckTaskDisposeTimeoutSeconds = 1;

        /// <summary>
        /// Timeout for read task wait operations (alias for ReadTaskDisposeTimeoutSeconds).
        /// </summary>
        public const int ReadTaskWaitSeconds = 2;

        /// <summary>
        /// Timeout for health check task wait operations (alias for HealthCheckTaskDisposeTimeoutSeconds).
        /// </summary>
        public const int HealthCheckWaitSeconds = 1;
    }

    /// <summary>
    /// Terminal stats collector defaults.
    /// </summary>
    public static class StatsCollectorDefaults
    {
        public const int CollectionIntervalSeconds = 1;
    }
}
