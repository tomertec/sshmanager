using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.Terminal.Services.Recording;

namespace SshManager.Terminal;

/// <summary>
/// Represents an active terminal session.
/// </summary>
public sealed class TerminalSession : IAsyncDisposable, IDisposable
{
    private readonly ILogger<TerminalSession> _logger;
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();

    public TerminalSession(ILogger<TerminalSession>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalSession>.Instance;
    }

    /// <summary>
    /// Display title for the session tab.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The host entry this session is connected to.
    /// </summary>
    public HostEntry? Host { get; set; }

    /// <summary>
    /// The active SSH connection (null if not connected or using external terminal).
    /// </summary>
    public ISshConnection? Connection { get; set; }

    /// <summary>
    /// SSH terminal bridge for data flow between SSH and terminal control.
    /// Note: Managed by SshTerminalControl, set here for reference/stats if needed.
    /// </summary>
    public SshTerminalBridge? Bridge { get; set; }

    /// <summary>
    /// The serial connection for this session (null if SSH connection).
    /// </summary>
    public ISerialConnection? SerialConnection { get; set; }

    /// <summary>
    /// The serial terminal bridge for this session (null if SSH connection).
    /// </summary>
    public SerialTerminalBridge? SerialBridge { get; set; }

    /// <summary>
    /// Gets whether this is a serial connection.
    /// </summary>
    public bool IsSerialSession => SerialConnection != null;

    /// <summary>
    /// Cancellation token source for the data receive loop.
    /// </summary>
    public CancellationTokenSource? ReceiveCts { get; set; }

    /// <summary>
    /// Whether this session is still active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Whether this session uses embedded terminal (true) or external terminal (false).
    /// </summary>
    public bool IsEmbedded { get; set; } = true;

    /// <summary>
    /// Decrypted password for the session (transient, never persisted).
    /// Stored as SecureString for enhanced security.
    /// </summary>
    public SecureString? DecryptedPassword { get; set; }

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Connection status message.
    /// </summary>
    public string Status { get; set; } = "Connecting...";

    /// <summary>
    /// Whether this session is selected for broadcast input.
    /// When broadcast mode is enabled, input will be sent to all selected sessions.
    /// </summary>
    public bool IsSelectedForBroadcast { get; set; }

    /// <summary>
    /// Whether the SSH or serial connection has been established.
    /// </summary>
    public bool IsConnected => Connection?.IsConnected == true || SerialConnection?.IsConnected == true;

    /// <summary>
    /// Session logger for recording terminal output (null if logging disabled).
    /// </summary>
    public SessionLogger? SessionLogger { get; set; }

    /// <summary>
    /// Log level for this session.
    /// </summary>
    public SessionLogLevel LogLevel { get; set; } = SessionLogLevel.OutputAndEvents;

    /// <summary>
    /// Whether to redact typed secrets from this session's log.
    /// </summary>
    public bool RedactTypedSecrets { get; set; }

    /// <summary>
    /// Session recorder for capturing terminal sessions in ASCIINEMA format (null if recording disabled).
    /// </summary>
    public SessionRecorder? SessionRecorder { get; set; }

    /// <summary>
    /// Whether this session is currently being recorded.
    /// </summary>
    public bool IsRecording => SessionRecorder?.IsRecording == true;

    /// <summary>
    /// Terminal statistics (uptime, latency, CPU/mem/disk usage, throughput).
    /// </summary>
    public TerminalStats Stats { get; } = new();

    /// <summary>
    /// Total bytes sent during this session.
    /// Thread-safe counter for throughput calculation.
    /// </summary>
    public long TotalBytesSent { get; set; }

    /// <summary>
    /// Total bytes received during this session.
    /// Thread-safe counter for throughput calculation.
    /// </summary>
    public long TotalBytesReceived { get; set; }

    /// <summary>
    /// Last few lines of terminal output for tooltip preview.
    /// Limited to ~200 characters for performance.
    /// </summary>
    private string _lastOutputPreview = string.Empty;
    public string LastOutputPreview
    {
        get => _lastOutputPreview;
        set
        {
            if (_lastOutputPreview != value)
            {
                _lastOutputPreview = value;
            }
        }
    }

    /// <summary>
    /// Display title that combines host information.
    /// </summary>
    public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : (Host != null ? $"{Host.Username}@{Host.Hostname}" : "Terminal");

    /// <summary>
    /// Duration since session was created (for tooltip display).
    /// </summary>
    public TimeSpan ConnectedDuration => DateTimeOffset.UtcNow - CreatedAt;

    /// <summary>
    /// Event raised when the session is closed.
    /// </summary>
    public event EventHandler? SessionClosed;

    /// <summary>
    /// Closes the session and disposes resources asynchronously.
    /// This is the preferred method for cleanup as it properly awaits async disposables.
    /// </summary>
    public async ValueTask CloseAsync()
    {
        if (_disposed) return;
        _disposed = true;

        IsActive = false;
        Status = "Disconnected";

        _logger.LogInformation("Closing terminal session {SessionId} ({Title})", Id, Title);

        // Cancel data receive loop
        try
        {
            ReceiveCts?.Cancel();
            ReceiveCts?.Dispose();
            ReceiveCts = null;
            _logger.LogDebug("Receive loop cancelled for session {SessionId}", Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cancelling receive loop for session {SessionId}", Id);
        }

        // Dispose connection asynchronously
        try
        {
            if (Connection != null)
            {
                await Connection.DisposeAsync();
                Connection = null;
                _logger.LogDebug("Connection disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing connection for session {SessionId}", Id);
        }

        // Clear bridge reference
        Bridge = null;

        // Dispose serial bridge asynchronously
        try
        {
            if (SerialBridge != null)
            {
                await SerialBridge.DisposeAsync();
                SerialBridge = null;
                _logger.LogDebug("Serial bridge disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing serial bridge for session {SessionId}", Id);
        }

        // Dispose serial connection asynchronously
        try
        {
            if (SerialConnection != null)
            {
                await SerialConnection.DisposeAsync();
                SerialConnection = null;
                _logger.LogDebug("Serial connection disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing serial connection for session {SessionId}", Id);
        }

        // Dispose session recorder asynchronously
        try
        {
            if (SessionRecorder != null)
            {
                await SessionRecorder.DisposeAsync();
                SessionRecorder = null;
                _logger.LogDebug("Session recorder disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing session recorder for session {SessionId}", Id);
        }

        // Dispose session logger asynchronously
        try
        {
            if (SessionLogger != null)
            {
                SessionLogger.LogEvent("SESSION", "Session closed");
                await SessionLogger.DisposeAsync();
                SessionLogger = null;
                _logger.LogDebug("Session logger disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing session logger for session {SessionId}", Id);
        }

        // Securely clear and dispose sensitive data
        try
        {
            if (DecryptedPassword != null)
            {
                DecryptedPassword.Dispose();
                DecryptedPassword = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing decrypted password for session {SessionId}", Id);
        }

        _logger.LogInformation("Terminal session {SessionId} closed", Id);
        SessionClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the session and disposes resources synchronously.
    /// Note: For proper async cleanup, prefer using CloseAsync or DisposeAsync.
    /// </summary>
    [Obsolete("Use CloseAsync instead to avoid potential deadlocks")]
    public void Close()
    {
        if (_disposed) return;
        _disposed = true;

        IsActive = false;
        Status = "Disconnected";

        _logger.LogInformation("Closing terminal session {SessionId} ({Title})", Id, Title);

        // Cancel data receive loop
        try
        {
            ReceiveCts?.Cancel();
            ReceiveCts?.Dispose();
            ReceiveCts = null;
            _logger.LogDebug("Receive loop cancelled for session {SessionId}", Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cancelling receive loop for session {SessionId}", Id);
        }

        // Dispose connection
        try
        {
            Connection?.Dispose();
            Connection = null;
            _logger.LogDebug("Connection disposed for session {SessionId}", Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing connection for session {SessionId}", Id);
        }

        // Clear bridge reference
        Bridge = null;

        // Dispose serial bridge - block on async disposal to ensure proper cleanup
        try
        {
            if (SerialBridge != null)
            {
                SerialBridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
                SerialBridge = null;
                _logger.LogDebug("Serial bridge disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing serial bridge for session {SessionId}", Id);
        }

        // Dispose serial connection - block on async disposal to ensure proper cleanup
        try
        {
            if (SerialConnection != null)
            {
                SerialConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
                SerialConnection = null;
                _logger.LogDebug("Serial connection disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing serial connection for session {SessionId}", Id);
        }

        // Dispose session recorder - block on async disposal to ensure proper cleanup
        try
        {
            if (SessionRecorder != null)
            {
                SessionRecorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
                SessionRecorder = null;
                _logger.LogDebug("Session recorder disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing session recorder for session {SessionId}", Id);
        }

        // Dispose session logger - block on async disposal to ensure proper cleanup
        try
        {
            if (SessionLogger != null)
            {
                SessionLogger.LogEvent("SESSION", "Session closed");
                SessionLogger.DisposeAsync().AsTask().GetAwaiter().GetResult();
                SessionLogger = null;
                _logger.LogDebug("Session logger disposed for session {SessionId}", Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing session logger for session {SessionId}", Id);
        }

        // Securely clear and dispose sensitive data
        try
        {
            if (DecryptedPassword != null)
            {
                DecryptedPassword.Dispose();
                DecryptedPassword = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing decrypted password for session {SessionId}", Id);
        }

        _logger.LogInformation("Terminal session {SessionId} closed", Id);
        SessionClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
#pragma warning disable CS0618 // Close() is obsolete but required for sync Dispose pattern
        Close();
#pragma warning restore CS0618
        GC.SuppressFinalize(this);
    }
}
