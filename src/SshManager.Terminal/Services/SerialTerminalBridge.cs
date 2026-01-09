using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Bridges serial port data with the terminal UI.
/// This class handles the bidirectional data flow between the serial port connection
/// and the terminal control.
/// </summary>
public sealed class SerialTerminalBridge : IAsyncDisposable, IDisposable
{
    private readonly Stream _stream;
    private readonly ILogger<SerialTerminalBridge> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readTask;
    private int _disposed;

    /// <summary>
    /// Event raised when data is received from the serial port.
    /// The byte array contains the raw terminal data.
    /// </summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>
    /// Event raised when the serial port connection is disconnected.
    /// </summary>
    public event Action? Disconnected;

    /// <summary>
    /// Gets the total number of bytes received through this bridge.
    /// </summary>
    public long TotalBytesReceived { get; private set; }

    /// <summary>
    /// Gets the total number of bytes sent through this bridge.
    /// </summary>
    public long TotalBytesSent { get; private set; }

    /// <summary>
    /// Gets or sets whether local echo is enabled.
    /// When enabled, sent data is echoed back to the DataReceived event.
    /// </summary>
    public bool LocalEcho { get; set; }

    /// <summary>
    /// Gets or sets the line ending to append when sending commands.
    /// Common values are "\r\n" (CRLF), "\r" (CR), or "\n" (LF).
    /// </summary>
    public string LineEnding { get; set; }

    /// <summary>
    /// Creates a new SerialTerminalBridge instance.
    /// </summary>
    /// <param name="stream">The stream connected to the serial port.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="localEcho">Whether to echo sent data locally.</param>
    /// <param name="lineEnding">The line ending to use for commands.</param>
    public SerialTerminalBridge(
        Stream stream,
        ILogger<SerialTerminalBridge>? logger = null,
        bool localEcho = false,
        string lineEnding = "\r\n")
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _logger = logger ?? NullLogger<SerialTerminalBridge>.Instance;
        LocalEcho = localEcho;
        LineEnding = lineEnding;
    }

    /// <summary>
    /// Starts the background task that reads data from the serial port.
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
        _logger.LogDebug("Serial terminal bridge read loop started");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await _stream.ReadAsync(buffer, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    _logger.LogInformation("Serial stream closed: {Message}", ex.Message);
                    break;
                }
                catch (TimeoutException)
                {
                    // Timeout is normal for serial ports, continue reading
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogDebug("Serial stream disposed");
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
                    _logger.LogInformation("Serial stream returned 0 bytes, connection closed");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in serial read loop");
        }
        finally
        {
            _logger.LogDebug("Serial terminal bridge read loop ended");
            try
            {
                Disconnected?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Disconnected handler");
            }
        }
    }

    /// <summary>
    /// Sends raw bytes to the serial port.
    /// </summary>
    /// <param name="data">The bytes to send.</param>
    public void SendData(byte[] data)
    {
        if (Volatile.Read(ref _disposed) != 0 || data.Length == 0) return;

        try
        {
            _stream.Write(data, 0, data.Length);
            _stream.Flush();
            TotalBytesSent += data.Length;

            // Echo locally if enabled
            if (LocalEcho)
            {
                try
                {
                    DataReceived?.Invoke(data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in DataReceived handler during local echo");
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to send {ByteCount} bytes to serial port", data.Length);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timeout sending {ByteCount} bytes to serial port", data.Length);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Serial stream disposed during send");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {ByteCount} bytes to serial port", data.Length);
        }
    }

    /// <summary>
    /// Sends a string to the serial port as UTF-8 encoded bytes.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public void SendText(string text)
    {
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrEmpty(text)) return;

        var bytes = Encoding.UTF8.GetBytes(text);
        SendData(bytes);
    }

    /// <summary>
    /// Sends a command followed by the configured line ending.
    /// </summary>
    /// <param name="command">The command to send.</param>
    public void SendCommand(string command)
    {
        SendText(command + LineEnding);
    }

    /// <summary>
    /// Asynchronously disposes the bridge, stopping the read loop.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _logger.LogDebug("Disposing serial terminal bridge");

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

    /// <summary>
    /// Disposes the bridge, stopping the read loop.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _logger.LogDebug("Disposing serial terminal bridge (sync)");

        _cts.Cancel();
        _cts.Dispose();
    }
}
