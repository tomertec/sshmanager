using System.IO;
using Microsoft.Extensions.Logging;
using RJCP.IO.Ports;

namespace SshManager.Terminal.Services;

/// <summary>
/// Represents an active serial port connection using SerialPortStream.
/// </summary>
/// <remarks>
/// <para>
/// This class wraps a SerialPortStream and implements ISerialConnection,
/// following the same pattern as SshConnection in the codebase.
/// </para>
/// <para>
/// <b>Threading:</b> The underlying SerialPortStream is thread-safe for
/// read/write operations. Error events fire on background threads.
/// </para>
/// <para>
/// <b>Resource Management:</b> Disposing this connection closes the serial port
/// and releases all associated resources.
/// </para>
/// </remarks>
public sealed class SerialConnection : ISerialConnection
{
    private readonly SerialPortStream _serialPort;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerialConnection"/> class.
    /// </summary>
    /// <param name="serialPort">The underlying serial port stream.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    internal SerialConnection(SerialPortStream serialPort, ILogger logger)
    {
        _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to error events
        _serialPort.ErrorReceived += OnErrorReceived;
    }

    /// <inheritdoc />
    public Stream BaseStream => _serialPort;

    /// <inheritdoc />
    public bool IsConnected => !_disposed && _serialPort.IsOpen;

    /// <inheritdoc />
    public bool IsOpen => _serialPort.IsOpen;

    /// <inheritdoc />
    public string PortName => _serialPort.PortName;

    /// <inheritdoc />
    public int BaudRate => _serialPort.BaudRate;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
    public void SendBreak(int duration = 250)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SerialConnection));
        }

        if (!_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open");
        }

        try
        {
            _serialPort.BreakState = true;
            Thread.Sleep(duration);
            _serialPort.BreakState = false;
            _logger.LogDebug("Sent break signal for {Duration}ms on {PortName}", duration, PortName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send break signal on {PortName}", PortName);
            throw;
        }
    }

    /// <inheritdoc />
    public void SetDtr(bool enabled)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SerialConnection));
        }

        if (!_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open");
        }

        try
        {
            _serialPort.DtrEnable = enabled;
            _logger.LogDebug("Set DTR to {State} on {PortName}", enabled ? "enabled" : "disabled", PortName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set DTR on {PortName}", PortName);
            throw;
        }
    }

    /// <inheritdoc />
    public void SetRts(bool enabled)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SerialConnection));
        }

        if (!_serialPort.IsOpen)
        {
            throw new InvalidOperationException("Serial port is not open");
        }

        try
        {
            _serialPort.RtsEnable = enabled;
            _logger.LogDebug("Set RTS to {State} on {PortName}", enabled ? "enabled" : "disabled", PortName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set RTS on {PortName}", PortName);
            throw;
        }
    }

    /// <summary>
    /// Handles errors from the serial port.
    /// </summary>
    private void OnErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
    {
        _logger.LogWarning("Serial port error on {PortName}: {ErrorType}", PortName, e.EventType);

        // Raise disconnected for serious errors
        if (e.EventType == SerialError.Frame ||
            e.EventType == SerialError.Overrun ||
            e.EventType == SerialError.RXOver ||
            e.EventType == SerialError.TXFull)
        {
            RaiseDisconnected();
        }
    }

    /// <summary>
    /// Raises the <see cref="Disconnected"/> event.
    /// </summary>
    private void RaiseDisconnected()
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the core disposal logic.
    /// </summary>
    private void DisposeCore()
    {
        _logger.LogDebug("Disposing serial connection on {PortName}", PortName);

        // Unsubscribe from events
        _serialPort.ErrorReceived -= OnErrorReceived;

        // Close and dispose the serial port
        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
                _logger.LogDebug("Serial port {PortName} closed", PortName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing serial port {PortName}", PortName);
        }

        try
        {
            _serialPort.Dispose();
            _logger.LogDebug("Serial port {PortName} disposed", PortName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing serial port {PortName}", PortName);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCore();

        _logger.LogInformation("Serial connection disposed on {PortName}", PortName);
        RaiseDisconnected();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Task.Run(Dispose);
    }
}
