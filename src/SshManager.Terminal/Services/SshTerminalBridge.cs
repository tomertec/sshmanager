using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;

namespace SshManager.Terminal.Services;

/// <summary>
/// Bridges SSH.NET ShellStream with EasyWindowsTerminalControl input/output.
/// This class handles the bidirectional data flow between the SSH connection
/// and the terminal control.
/// </summary>
public sealed class SshTerminalBridge : IAsyncDisposable, IDisposable
{
    // Not owned by this class; lifecycle is managed by SshConnection.
    private ShellStream? _shellStream;
    private readonly ILogger<SshTerminalBridge> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<bool>? _connectionHealthCheck;
    private readonly TimeSpan _healthCheckInterval;
    private Task? _readTask;
    private Task? _healthCheckTask;
    private int _disposed = 0;

    /// <summary>
    /// Default interval for connection health checks (10 seconds).
    /// This value balances timely detection of stale connections with minimal network overhead.
    /// Too frequent: wastes bandwidth and CPU; too infrequent: delays detection of dead connections.
    /// For user-configurable health check intervals, see AppSettings (if implemented in future).
    /// </summary>
    public static readonly TimeSpan DefaultHealthCheckInterval = TimeSpan.FromSeconds(TerminalConstants.SshDefaults.HealthCheckIntervalSeconds);

    /// <summary>
    /// Event raised when data is received from the SSH server.
    /// The byte array contains the raw terminal data.
    /// </summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// Event raised when the SSH connection is disconnected.
    /// </summary>
    public event EventHandler? Disconnected;

    private long _totalBytesSent;
    private long _totalBytesReceived;

    /// <summary>
    /// Gets the total number of bytes sent through this bridge.
    /// </summary>
    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);

    /// <summary>
    /// Gets the total number of bytes received through this bridge.
    /// </summary>
    public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);

    /// <summary>
    /// Creates a new SSH terminal bridge.
    /// </summary>
    /// <param name="shellStream">The SSH shell stream for I/O.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="connectionHealthCheck">
    /// Optional callback to check if the connection is still alive.
    /// Should return true if connected, false if disconnected.
    /// When provided, the bridge will periodically call this to detect stale connections
    /// (e.g., when a remote host reboots during an idle session).
    /// </param>
    /// <param name="healthCheckInterval">
    /// Interval between health checks. Defaults to 15 seconds.
    /// </param>
    public SshTerminalBridge(
        ShellStream shellStream,
        ILogger<SshTerminalBridge>? logger = null,
        Func<bool>? connectionHealthCheck = null,
        TimeSpan? healthCheckInterval = null)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _logger = logger ?? NullLogger<SshTerminalBridge>.Instance;
        _connectionHealthCheck = connectionHealthCheck;
        _healthCheckInterval = healthCheckInterval ?? DefaultHealthCheckInterval;
    }

    /// <summary>
    /// Starts the background task that reads data from the SSH server.
    /// Data is delivered via the <see cref="DataReceived"/> event.
    /// Also starts the connection health check task if a health check callback was provided.
    /// </summary>
    public void StartReading()
    {
        if (_readTask != null)
        {
            _logger.LogWarning("StartReading called but read task already running");
            return;
        }

        _readTask = ReadLoopAsync(_cts.Token);
        _logger.LogDebug("SSH terminal bridge read loop started");

        // Start health check task if callback provided
        if (_connectionHealthCheck != null)
        {
            _healthCheckTask = ConnectionHealthCheckLoopAsync(_cts.Token);
            _logger.LogDebug("SSH connection health check started with {Interval}s interval",
                _healthCheckInterval.TotalSeconds);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    var stream = _shellStream;
                    if (stream == null) break;
                    bytesRead = await stream.ReadAsync(buffer, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException)
                {
                    _logger.LogInformation("SSH stream closed: {Message}", ex.Message);
                    break;
                }

                if (bytesRead > 0)
                {
                    Interlocked.Add(ref _totalBytesReceived, bytesRead);

                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);

                    try
                    {
                        DataReceived?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in DataReceived handler");
                    }
                }
                else if (bytesRead == 0)
                {
                    // Stream closed
                    _logger.LogInformation("SSH stream returned 0 bytes, connection closed");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SSH read loop");
        }
        finally
        {
            _logger.LogDebug("SSH terminal bridge read loop ended");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Periodically checks if the connection is still alive.
    /// If the connection health check returns false, cancels the read loop.
    /// </summary>
    private async Task ConnectionHealthCheckLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_healthCheckInterval, ct);

                if (ct.IsCancellationRequested) break;

                try
                {
                    var isConnected = _connectionHealthCheck?.Invoke() ?? true;
                    if (!isConnected)
                    {
                        _logger.LogInformation("Connection health check failed - connection is no longer alive");
                        // Dispose the shell stream to force ReadAsync to abort.
                        // SSH.NET's ReadAsync doesn't properly support CancellationToken,
                        // so we must close the stream to unblock the read operation.
                        ForceCloseStream();
                        await _cts.CancelAsync();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Connection health check threw exception - assuming disconnected");
                    ForceCloseStream();
                    await _cts.CancelAsync();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check loop ended with exception");
        }

        _logger.LogDebug("Connection health check loop ended");
    }

    /// <summary>
    /// Sends raw bytes to the SSH server.
    /// </summary>
    /// <param name="data">The bytes to send.</param>
    public void SendData(byte[] data)
    {
        if (System.Threading.Volatile.Read(ref _disposed) != 0 || data.Length == 0) return;

        var stream = _shellStream;
        if (stream == null) return;

        try
        {
            stream.Write(data, 0, data.Length);
            stream.Flush();
            Interlocked.Add(ref _totalBytesSent, data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {ByteCount} bytes to SSH", data.Length);
        }
    }

    /// <summary>
    /// Sends a character span to the SSH server as UTF-8 encoded bytes.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public void SendData(ReadOnlySpan<char> text)
    {
        if (System.Threading.Volatile.Read(ref _disposed) != 0 || text.IsEmpty) return;

        var bytes = Encoding.UTF8.GetBytes(text.ToArray());
        SendData(bytes);
    }

    /// <summary>
    /// Sends a string to the SSH server as UTF-8 encoded bytes.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public void SendText(string text)
    {
        if (System.Threading.Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(text)) return;

        var bytes = Encoding.UTF8.GetBytes(text);
        SendData(bytes);
    }

    /// <summary>
    /// Sends a command followed by a carriage return.
    /// </summary>
    /// <param name="command">The command to send.</param>
    public void SendCommand(string command)
    {
        SendText(command + "\r");
    }

    /// <summary>
    /// Forcefully closes the shell stream to abort any blocking read operations.
    /// This is needed because SSH.NET's ReadAsync doesn't properly respect CancellationToken.
    /// </summary>
    private void ForceCloseStream()
    {
        try
        {
            _shellStream?.Close();
            _logger.LogDebug("Shell stream forcefully closed to abort blocking read");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while force-closing shell stream (expected if already closed)");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _logger.LogDebug("Disposing SSH terminal bridge");

        await _cts.CancelAsync();

        // Wait for read task to complete gracefully during dispose
        // The 2-second timeout is a technical safeguard - the task should respond quickly
        // to cancellation, but we don't want to block indefinitely if it's stuck
        if (_readTask != null)
        {
            try
            {
                await _readTask.WaitAsync(TimeSpan.FromSeconds(TerminalConstants.BridgeDefaults.ReadTaskDisposeTimeoutSeconds));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Read task did not complete within {Timeout}s timeout", TerminalConstants.BridgeDefaults.ReadTaskDisposeTimeoutSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Read task ended with exception during dispose");
            }
        }

        // Wait for health check task to complete
        // Uses shorter 1-second timeout as health checks are simpler and should terminate faster
        if (_healthCheckTask != null)
        {
            try
            {
                await _healthCheckTask.WaitAsync(TimeSpan.FromSeconds(TerminalConstants.BridgeDefaults.HealthCheckTaskDisposeTimeoutSeconds));
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("Health check task did not complete within {Timeout}s timeout", TerminalConstants.BridgeDefaults.HealthCheckTaskDisposeTimeoutSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health check task ended with exception during dispose");
            }
        }

        _shellStream = null;
        _cts.Dispose();
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _logger.LogDebug("Disposing SSH terminal bridge (sync)");

        _cts.Cancel();

        // Wait for background tasks to complete before disposing CTS,
        // otherwise they may access the disposed CancellationToken.
        try { _readTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch { /* Task may have faulted or been cancelled */ }

        try { _healthCheckTask?.Wait(TimeSpan.FromSeconds(1)); }
        catch { /* Task may have faulted or been cancelled */ }

        _shellStream = null;
        _cts.Dispose();
    }
}
