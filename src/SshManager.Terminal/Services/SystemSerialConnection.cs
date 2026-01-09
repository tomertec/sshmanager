using System.IO;
using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace SshManager.Terminal.Services;

/// <summary>
/// Serial connection implementation using System.IO.Ports.SerialPort.
/// This is more compatible with USB serial adapters on Windows.
/// </summary>
public sealed class SystemSerialConnection : ISerialConnection
{
    private readonly SerialPort _serialPort;
    private readonly ILogger _logger;
    private bool _disposed;

    internal SystemSerialConnection(SerialPort serialPort, ILogger logger)
    {
        _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serialPort.ErrorReceived += OnErrorReceived;
    }

    public Stream BaseStream => _serialPort.BaseStream;
    public bool IsConnected => !_disposed && _serialPort.IsOpen;
    public bool IsOpen => _serialPort.IsOpen;
    public string PortName => _serialPort.PortName;
    public int BaudRate => _serialPort.BaudRate;
    public event EventHandler? Disconnected;

    public void SendBreak(int duration = 250)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SystemSerialConnection));
        if (!_serialPort.IsOpen) throw new InvalidOperationException("Serial port is not open");

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

    public void SetDtr(bool enabled)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SystemSerialConnection));
        if (!_serialPort.IsOpen) throw new InvalidOperationException("Serial port is not open");

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

    public void SetRts(bool enabled)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SystemSerialConnection));
        if (!_serialPort.IsOpen) throw new InvalidOperationException("Serial port is not open");

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

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        _logger.LogWarning("Serial port error on {PortName}: {ErrorType}", PortName, e.EventType);
        if (e.EventType == SerialError.Frame ||
            e.EventType == SerialError.Overrun ||
            e.EventType == SerialError.RXOver ||
            e.EventType == SerialError.TXFull)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _serialPort.ErrorReceived -= OnErrorReceived;
        try
        {
            if (_serialPort.IsOpen) _serialPort.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing serial port {PortName}", PortName);
        }

        try
        {
            _serialPort.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing serial port {PortName}", PortName);
        }

        _logger.LogInformation("Serial connection disposed on {PortName}", PortName);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(Dispose);
    }
}
