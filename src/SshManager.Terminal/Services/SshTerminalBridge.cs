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
    private readonly ShellStream _shellStream;
    private readonly ILogger<SshTerminalBridge> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readTask;
    private bool _disposed;

    /// <summary>
    /// Event raised when data is received from the SSH server.
    /// The byte array contains the raw terminal data.
    /// </summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// Event raised when the SSH connection is disconnected.
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Gets the total number of bytes sent through this bridge.
    /// </summary>
    public long TotalBytesSent { get; private set; }

    /// <summary>
    /// Gets the total number of bytes received through this bridge.
    /// </summary>
    public long TotalBytesReceived { get; private set; }

    public SshTerminalBridge(ShellStream shellStream, ILogger<SshTerminalBridge>? logger = null)
    {
        _shellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        _logger = logger ?? NullLogger<SshTerminalBridge>.Instance;
    }

    /// <summary>
    /// Starts the background task that reads data from the SSH server.
    /// Data is delivered via the <see cref="DataReceived"/> event.
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
                    bytesRead = await _shellStream.ReadAsync(buffer, ct);
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
                    TotalBytesReceived += bytesRead;

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
    /// Sends raw bytes to the SSH server.
    /// </summary>
    /// <param name="data">The bytes to send.</param>
    public void SendData(byte[] data)
    {
        if (_disposed || data.Length == 0) return;

        try
        {
            _shellStream.Write(data, 0, data.Length);
            _shellStream.Flush();
            TotalBytesSent += data.Length;
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
        if (_disposed || text.IsEmpty) return;

        var bytes = Encoding.UTF8.GetBytes(text.ToArray());
        SendData(bytes);
    }

    /// <summary>
    /// Sends a string to the SSH server as UTF-8 encoded bytes.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public void SendText(string text)
    {
        if (_disposed || string.IsNullOrEmpty(text)) return;

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing SSH terminal bridge");

        await _cts.CancelAsync();

        if (_readTask != null)
        {
            try
            {
                await _readTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Read task did not complete within timeout");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Read task ended with exception during dispose");
            }
        }

        _cts.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing SSH terminal bridge (sync)");

        _cts.Cancel();
        _cts.Dispose();
    }
}
