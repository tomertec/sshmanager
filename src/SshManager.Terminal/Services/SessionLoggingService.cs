using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for logging terminal session output to files.
/// </summary>
public class SessionLoggingService : ISessionLoggingService
{
    private readonly ConcurrentDictionary<Guid, SessionLogger> _loggers = new();
    private readonly ILogger<SessionLoggingService> _logger;

    private string _logDirectory;
    private bool _timestampEachLine = true;
    private int _maxLogFileSizeMB = 50;
    private int _maxLogFilesToKeep = 5;

    public SessionLoggingService(ILogger<SessionLoggingService>? logger = null)
    {
        _logger = logger ?? NullLogger<SessionLoggingService>.Instance;
        _logDirectory = GetDefaultLogDirectory();
    }

    public string GetDefaultLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SshManager",
            "sessions");
    }

    /// <summary>
    /// Gets the current log directory.
    /// </summary>
    public string GetLogDirectory()
    {
        return _logDirectory;
    }

    /// <summary>
    /// Sets the log directory for session logs.
    /// </summary>
    public void SetLogDirectory(string directory)
    {
        _logDirectory = directory;
    }

    /// <summary>
    /// Sets whether to timestamp each line.
    /// </summary>
    public void SetTimestampEachLine(bool value)
    {
        _timestampEachLine = value;
    }

    /// <summary>
    /// Sets the maximum log file size in MB before rotation.
    /// </summary>
    public void SetMaxLogFileSizeMB(int sizeMB)
    {
        _maxLogFileSizeMB = sizeMB > 0 ? sizeMB : 50;
    }

    /// <summary>
    /// Sets the maximum number of rotated log files to keep.
    /// </summary>
    public void SetMaxLogFilesToKeep(int count)
    {
        _maxLogFilesToKeep = count > 0 ? count : 5;
    }

    public SessionLogger StartLogging(Guid sessionId, string sessionTitle, SessionLogLevel logLevel, bool redactTypedSecrets)
    {
        // Create log file name with timestamp
        var sanitizedTitle = SanitizeFileName(sessionTitle);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{sanitizedTitle}_{timestamp}.log";
        var logPath = Path.Combine(_logDirectory, fileName);

        _logger.LogInformation("Starting session logging for {SessionId} to {LogPath}", sessionId, logPath);

        var sessionLogger = new SessionLogger(
            sessionId,
            logPath,
            _timestampEachLine,
            logLevel,
            redactTypedSecrets,
            _maxLogFileSizeMB,
            _maxLogFilesToKeep);
        _loggers[sessionId] = sessionLogger;

        return sessionLogger;
    }

    public void StopLogging(Guid sessionId)
    {
        if (_loggers.TryRemove(sessionId, out var logger))
        {
            _logger.LogInformation("Stopping session logging for {SessionId}", sessionId);
            Task.Run(async () =>
            {
                try { await logger.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Error disposing session logger"); }
            }).Wait(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Gets an existing logger for a session.
    /// </summary>
    public SessionLogger? GetLogger(Guid sessionId)
    {
        return _loggers.TryGetValue(sessionId, out var logger) ? logger : null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            name = name.Replace(c, '_');
        }
        // Limit length
        if (name.Length > 50)
        {
            name = name[..50];
        }
        return name;
    }
}
