namespace SshManager.Core;

/// <summary>
/// Public constants shared across the SshManager application.
/// These values are safe to reference from any project.
/// </summary>
public static class Constants
{
    /// <summary>
    /// String length limits for model validation.
    /// </summary>
    public static class StringLimits
    {
        public const int MaxHostnameLength = 400;
        public const int MaxUsernameLength = 100;
        public const int MaxDisplayNameLength = 200;
        public const int MaxNotesLength = 5000;
        public const int MaxPathLength = 1000;
        public const int MaxSecureNotesLength = 10000;
        public const int MaxCommandLength = 4000;
        public const int MaxLabelLength = 100;
        public const int MaxBindAddressLength = 100;
        public const int MaxEnvironmentVariableNameLength = 100;
        public const int MaxEnvironmentVariableValueLength = 4096;
        public const int MaxSnippetNameLength = 100;
    }

    /// <summary>
    /// Network and connection related constants.
    /// </summary>
    public static class Network
    {
        public const int DefaultSshPort = 22;
        public const int MinPort = 1;
        public const int MaxPort = 65535;
        public const int DefaultBaudRate = SerialDefaults.DefaultBaudRate;
        public const int DefaultDataBits = SerialDefaults.DefaultDataBits;
        public const int RfcMaxHostnameLength = 253;
    }

    /// <summary>
    /// Terminal configuration defaults.
    /// </summary>
    public static class TerminalDefaults
    {
        public const int DefaultScrollbackBufferSize = 10000;
        public const int DefaultTerminalBufferInMemoryLines = 5000;
        public const int MinScrollbackLines = 100;
        public const int MaxScrollbackLines = 100000;
        public const int MinFontSize = 6;
        public const int MaxFontSize = 72;
        public const int DefaultFontSize = 14;
        public const string DefaultFontFamily = "Cascadia Mono";
        public const string DefaultTerminalName = "xterm-256color";
    }

    /// <summary>
    /// Connection and reconnection timing defaults.
    /// </summary>
    public static class ConnectionDefaults
    {
        public const int DefaultConnectionTimeoutSeconds = 30;
        public const int DefaultKeepAliveIntervalSeconds = 60;
        public const int MaxReconnectAttempts = 3;
        public const int ReconnectBaseDelayMs = 1000;
        public const int ReconnectMaxDelayMs = 30000;
        public const int ConnectionPoolIdleTimeoutSeconds = 300;
        public const int ConnectionPoolMaxPerHost = 3;
        public const int CredentialCacheTimeoutMinutes = 15;
        public const int ConnectionHistoryRetentionDays = 90;
        public const int MaxHistoryEntries = 100;

        /// <summary>
        /// Connection lock semaphore timeout in seconds.
        /// This timeout prevents deadlocks when multiple connection attempts occur simultaneously.
        /// A value of 30s is sufficient for normal UI interactions while preventing indefinite blocking.
        /// </summary>
        public const int ConnectionLockTimeoutSeconds = 30;

        /// <summary>
        /// Host status check timeout constants.
        /// These are optimized for quick reachability checks without blocking the UI.
        /// </summary>
        public static class StatusCheckTimeouts
        {
            /// <summary>
            /// ICMP ping timeout in milliseconds.
            /// 1000ms provides fast response while allowing time for network latency.
            /// </summary>
            public const int PingTimeoutMs = 1000;

            /// <summary>
            /// TCP connection timeout for status checks in milliseconds.
            /// 1500ms allows slightly more time than ping for TCP handshake completion.
            /// </summary>
            public const int TcpTimeoutMs = 1500;
        }
    }

    /// <summary>
    /// Buffer and performance related constants.
    /// </summary>
    public static class BufferSizes
    {
        public const int DefaultBufferSize = 4096;
        public const int TerminalOutputMaxBatchSize = 8192;
        public const int MinBatchSize = 1024;
        public const int MaxBatchSize = 65536;
        public const int TerminalOutputFlushIntervalMs = 16;
        public const int MinFlushIntervalMs = 8;
        public const int MaxFlushIntervalMs = 100;
    }

    /// <summary>
    /// UI and animation timing.
    /// </summary>
    public static class UiDefaults
    {
        public const int MinimumPaneSize = 100;
        public const int AutocompletionDebounceMs = 150;
        public const int MinAutocompletionDebounceMs = 50;
        public const int MaxAutocompletionDebounceMs = 1000;
        public const int MaxCompletionSuggestions = 10;
        public const int MinCompletionSuggestions = 5;
        public const int MaxCompletionSuggestionsLimit = 50;
    }

    /// <summary>
    /// File and backup related constants.
    /// </summary>
    public static class FileDefaults
    {
        public const int MaxLogFileSizeMB = 50;
        public const int MaxLogFilesToKeep = 5;
        public const int BackupIntervalMinutes = 60;
        public const int MaxBackupCount = 10;
        public const int SyncIntervalMinutes = 5;
    }

    /// <summary>
    /// Status check defaults.
    /// </summary>
    public static class StatusDefaults
    {
        public const int DefaultStatusCheckIntervalSeconds = 30;
        public const int DefaultMaxConcurrency = 10;
        public const int PingTimeoutMs = ConnectionDefaults.StatusCheckTimeouts.PingTimeoutMs;
        public const int TcpTimeoutMs = ConnectionDefaults.StatusCheckTimeouts.TcpTimeoutMs;
    }

    /// <summary>
    /// Serial port default values.
    /// </summary>
    public static class SerialDefaults
    {
        public const int DefaultBaudRate = 9600;
        public const int DefaultDataBits = 8;
        public const string DefaultLineEnding = "\r\n";
    }

    /// <summary>
    /// X11 forwarding defaults.
    /// </summary>
    public static class X11Defaults
    {
        public const int DefaultDisplayNumber = 0;
        public const string LocalBindAddress = "127.0.0.1";
    }

    /// <summary>
    /// Database and storage defaults.
    /// </summary>
    public static class DatabaseDefaults
    {
        public const int MaxCommandHistoryEntries = 100;
        public const int CommandHistoryRetentionDays = 0;
    }
}
