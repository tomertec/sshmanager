using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SshManager.Terminal.Services;

/// <summary>
/// Log level for session logging.
/// </summary>
public enum SessionLogLevel
{
    /// <summary>
    /// Log session output and events.
    /// </summary>
    OutputAndEvents,
    /// <summary>
    /// Log only session events.
    /// </summary>
    EventsOnly,
    /// <summary>
    /// Log only error events.
    /// </summary>
    ErrorsOnly
}

/// <summary>
/// Service for logging terminal session output to files.
/// </summary>
public interface ISessionLoggingService
{
    /// <summary>
    /// Starts logging for a session.
    /// </summary>
    SessionLogger StartLogging(Guid sessionId, string sessionTitle, SessionLogLevel logLevel, bool redactTypedSecrets);

    /// <summary>
    /// Stops logging for a session.
    /// </summary>
    void StopLogging(Guid sessionId);

    /// <summary>
    /// Gets the default log directory path.
    /// </summary>
    string GetDefaultLogDirectory();

    /// <summary>
    /// Gets the current log directory path.
    /// </summary>
    string GetLogDirectory();

    /// <summary>
    /// Sets the log directory for session logs.
    /// </summary>
    void SetLogDirectory(string directory);

    /// <summary>
    /// Sets whether to timestamp each line.
    /// </summary>
    void SetTimestampEachLine(bool value);

    /// <summary>
    /// Sets the maximum log file size in MB before rotation.
    /// </summary>
    void SetMaxLogFileSizeMB(int sizeMB);

    /// <summary>
    /// Sets the maximum number of rotated log files to keep.
    /// </summary>
    void SetMaxLogFilesToKeep(int count);

    /// <summary>
    /// Gets an existing logger for a session.
    /// </summary>
    SessionLogger? GetLogger(Guid sessionId);
}

/// <summary>
/// Logger instance for a single terminal session.
/// </summary>
public class SessionLogger : IAsyncDisposable
{
    private StreamWriter _writer;
    private readonly bool _timestampEachLine;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxFilesToKeep;
    private readonly object _lock = new();
    private bool _disposed;
    private string _currentLogFilePath;
    private readonly StringBuilder _inputBuffer = new();
    private readonly Queue<string> _recentInputs = new();
    private const int MaxRedactionLines = 50;

    public Guid SessionId { get; }
    public string LogFilePath => _currentLogFilePath;
    public bool IsLogging => !_disposed;
    public SessionLogLevel LogLevel { get; set; }
    public bool RedactTypedSecrets { get; set; }

    public SessionLogger(
        Guid sessionId,
        string logFilePath,
        bool timestampEachLine,
        SessionLogLevel logLevel,
        bool redactTypedSecrets,
        int maxFileSizeMB = 50,
        int maxFilesToKeep = 5)
    {
        SessionId = sessionId;
        _currentLogFilePath = logFilePath;
        _timestampEachLine = timestampEachLine;
        LogLevel = logLevel;
        RedactTypedSecrets = redactTypedSecrets;
        _maxFileSizeBytes = maxFileSizeMB * 1024L * 1024L;
        _maxFilesToKeep = maxFilesToKeep;

        // Create directory if needed
        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Open file for append with auto-flush
        _writer = new StreamWriter(logFilePath, append: true)
        {
            AutoFlush = true
        };

        // Write session header
        _writer.WriteLine($"=== Session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    /// <summary>
    /// Logs raw terminal data.
    /// </summary>
    public void LogData(byte[] data)
    {
        if (_disposed) return;
        if (LogLevel is SessionLogLevel.EventsOnly or SessionLogLevel.ErrorsOnly) return;

        lock (_lock)
        {
            try
            {
                // Check if rotation is needed before writing
                CheckAndRotateIfNeeded();

                var text = System.Text.Encoding.UTF8.GetString(data);

                if (_timestampEachLine)
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    var lines = text.Split('\n');
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            var outputLine = StripAnsiCodes(line.TrimEnd('\r'));
                            if (RedactTypedSecrets)
                            {
                                outputLine = RedactLine(outputLine);
                            }
                            _writer.Write($"[{timestamp}] ");
                            _writer.WriteLine(outputLine);
                        }
                    }
                }
                else
                {
                    var outputText = StripAnsiCodes(text);
                    if (RedactTypedSecrets)
                    {
                        outputText = RedactText(outputText);
                    }
                    _writer.Write(outputText);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }

    /// <summary>
    /// Logs an event (connect, disconnect, etc).
    /// </summary>
    public void LogEvent(string eventType, string message)
    {
        if (_disposed) return;
        if (LogLevel == SessionLogLevel.ErrorsOnly && !IsErrorEvent(eventType)) return;

        lock (_lock)
        {
            try
            {
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{eventType}] {message}");
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }

    /// <summary>
    /// Records user input for redaction purposes.
    /// </summary>
    public void RecordInput(byte[] data)
    {
        if (_disposed || !RedactTypedSecrets || data.Length == 0) return;

        lock (_lock)
        {
            foreach (var b in data)
            {
                switch (b)
                {
                    case (byte)'\r':
                    case (byte)'\n':
                        FinalizeInputLine();
                        break;
                    case 0x7F: // DEL (backspace)
                    case 0x08: // BS
                        if (_inputBuffer.Length > 0)
                        {
                            _inputBuffer.Length -= 1;
                        }
                        break;
                    default:
                        if (b >= 0x20 && b < 0x7F)
                        {
                            _inputBuffer.Append((char)b);
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Strips ANSI escape codes from text for cleaner logs.
    /// </summary>
    private static string StripAnsiCodes(string text)
    {
        // Remove ANSI escape sequences: ESC [ ... letter
        return System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", "");
    }

    private void FinalizeInputLine()
    {
        if (_inputBuffer.Length == 0)
        {
            return;
        }

        var line = _inputBuffer.ToString().Trim();
        _inputBuffer.Clear();

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _recentInputs.Enqueue(line);
        while (_recentInputs.Count > MaxRedactionLines)
        {
            _recentInputs.Dequeue();
        }
    }

    private string RedactText(string text)
    {
        if (_recentInputs.Count == 0)
        {
            return text;
        }

        var redacted = text;
        foreach (var token in _recentInputs)
        {
            if (!string.IsNullOrEmpty(token))
            {
                redacted = redacted.Replace(token, "[REDACTED]", StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    private string RedactLine(string line)
    {
        return RedactText(line);
    }

    private static bool IsErrorEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        return eventType.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
               eventType.Equals("FAIL", StringComparison.OrdinalIgnoreCase) ||
               eventType.Equals("FAILED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks current log file size and rotates if it exceeds the maximum.
    /// </summary>
    private void CheckAndRotateIfNeeded()
    {
        try
        {
            var fileInfo = new FileInfo(_currentLogFilePath);
            if (!fileInfo.Exists || fileInfo.Length < _maxFileSizeBytes)
            {
                return;
            }

            // Close current writer
            _writer.WriteLine($"=== Log rotated at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _writer.Flush();
            _writer.Dispose();

            // Rotate existing files
            RotateLogFiles();

            // Reopen writer on new file
            _writer = new StreamWriter(_currentLogFilePath, append: false)
            {
                AutoFlush = true
            };
            _writer.WriteLine($"=== Log continued at {DateTime.Now:yyyy-MM-dd HH:mm:ss} (rotated) ===");
        }
        catch
        {
            // Ignore rotation errors - continue logging to current file
        }
    }

    /// <summary>
    /// Rotates log files by renaming current.log to current.1.log, etc.
    /// </summary>
    private void RotateLogFiles()
    {
        var basePath = _currentLogFilePath;
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);

        // Delete oldest file if we're at max
        var oldestPath = Path.Combine(dir, $"{fileNameWithoutExt}.{_maxFilesToKeep}{ext}");
        if (File.Exists(oldestPath))
        {
            File.Delete(oldestPath);
        }

        // Shift existing rotated files
        for (int i = _maxFilesToKeep - 1; i >= 1; i--)
        {
            var fromPath = Path.Combine(dir, $"{fileNameWithoutExt}.{i}{ext}");
            var toPath = Path.Combine(dir, $"{fileNameWithoutExt}.{i + 1}{ext}");
            if (File.Exists(fromPath))
            {
                File.Move(fromPath, toPath, overwrite: true);
            }
        }

        // Rename current file to .1
        var rotatedPath = Path.Combine(dir, $"{fileNameWithoutExt}.1{ext}");
        if (File.Exists(basePath))
        {
            File.Move(basePath, rotatedPath, overwrite: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            try
            {
                _writer.WriteLine($"=== Session ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _writer.WriteLine();
            }
            catch
            {
                // Ignore
            }
        }

        await _writer.DisposeAsync();
    }
}
