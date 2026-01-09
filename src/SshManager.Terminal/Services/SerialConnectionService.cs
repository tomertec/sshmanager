using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RJCP.IO.Ports;
using SshManager.Terminal.Models;
using SysSerialPort = System.IO.Ports.SerialPort;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for establishing serial port connections using RJCP.SerialPortStream.
/// </summary>
/// <remarks>
/// <para>
/// This service handles the complexity of serial port connection establishment including:
/// - Port configuration (baud rate, data bits, parity, stop bits)
/// - Flow control settings (hardware RTS/CTS, software XON/XOFF)
/// - DTR/RTS signal initialization
/// - Buffer size configuration
/// </para>
/// <para>
/// <b>Threading:</b> All public methods are async-safe. The port open operation
/// runs on a background thread to avoid blocking the UI.
/// </para>
/// <para>
/// <b>Resource Management:</b> Returns ISerialConnection which owns the port.
/// Callers must dispose the connection when done.
/// </para>
/// </remarks>
public sealed class SerialConnectionService : ISerialConnectionService
{
    private readonly ILogger<SerialConnectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerialConnectionService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SerialConnectionService(ILogger<SerialConnectionService> logger)
    {
        _logger = logger ?? NullLogger<SerialConnectionService>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The connection process:
    /// <list type="number">
    /// <item>Creates a SerialPortStream with the specified settings</item>
    /// <item>Opens the port on a background thread</item>
    /// <item>Validates the port opened successfully</item>
    /// <item>Returns a SerialConnection wrapping the port</item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task<ISerialConnection> ConnectAsync(SerialConnectionInfo info, CancellationToken ct = default)
    {
        ValidateConnectionInfo(info);

        _logger.LogInformation(
            "Connecting to serial port {PortName} at {BaudRate} baud ({DataBits}{Parity}{StopBits})",
            info.PortName,
            info.BaudRate,
            info.DataBits,
            GetParityChar(info.Parity),
            GetStopBitsString(info.StopBits));

        // Try System.IO.Ports first (more compatible with USB adapters), then fall back to RJCP
        try
        {
            return await ConnectWithSystemSerialPortAsync(info, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "System.IO.Ports failed for {PortName}, trying RJCP.SerialPortStream", info.PortName);
        }

        // Fallback to RJCP.SerialPortStream
        SerialPortStream? port = null;
        try
        {
            port = new SerialPortStream(info.PortName);
            await Task.Run(() => port.Open(), ct);

            // Configure settings after opening
            await Task.Run(() =>
            {
                TrySetProperty(() => port.BaudRate = info.BaudRate, "BaudRate", info.PortName);
                TrySetProperty(() => port.DataBits = info.DataBits, "DataBits", info.PortName);
                TrySetProperty(() => port.Parity = info.Parity, "Parity", info.PortName);
                TrySetProperty(() => port.StopBits = info.StopBits, "StopBits", info.PortName);
                TrySetProperty(() => port.Handshake = info.Handshake, "Handshake", info.PortName);
                TrySetProperty(() => port.ReadTimeout = info.ReadTimeout, "ReadTimeout", info.PortName);
                TrySetProperty(() => port.WriteTimeout = info.WriteTimeout, "WriteTimeout", info.PortName);
            }, ct);

            TrySetProperty(() => port.DtrEnable = info.DtrEnable, "DtrEnable", info.PortName);
            if (info.Handshake != Handshake.Rts && info.Handshake != Handshake.RtsXOn)
            {
                TrySetProperty(() => port.RtsEnable = info.RtsEnable, "RtsEnable", info.PortName);
            }

            if (!port.IsOpen)
            {
                throw new InvalidOperationException($"Failed to open serial port {info.PortName}");
            }

            _logger.LogInformation("Serial port {PortName} opened with RJCP at {BaudRate} baud", info.PortName, info.BaudRate);
            return new SerialConnection(port, _logger);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogConnectionFailure(info, ex, "Port may be in use by another application");
            port?.Dispose();
            throw new InvalidOperationException(
                $"Access denied to serial port {info.PortName}. The port may be in use by another application.",
                ex);
        }
        catch (IOException ex)
        {
            LogConnectionFailure(info, ex, "I/O error occurred");
            port?.Dispose();
            throw new InvalidOperationException(
                $"I/O error opening serial port {info.PortName}: {ex.Message}",
                ex);
        }
        catch (ArgumentException ex)
        {
            LogConnectionFailure(info, ex, "Invalid port configuration");
            port?.Dispose();
            throw new ArgumentException(
                $"Invalid configuration for serial port {info.PortName}: {ex.Message}",
                nameof(info),
                ex);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Serial port connection to {PortName} was cancelled", info.PortName);
            port?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            LogConnectionFailure(info, ex, null);
            port?.Dispose();
            throw new InvalidOperationException(
                $"Failed to open serial port {info.PortName}: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public string[] GetAvailablePorts()
    {
        try
        {
            // Use System.IO.Ports.SerialPort.GetPortNames() to enumerate available ports
            // This is the standard .NET method and works reliably on Windows
            var ports = System.IO.Ports.SerialPort.GetPortNames();
            _logger.LogDebug("Found {Count} available serial port(s): {Ports}",
                ports.Length,
                ports.Length > 0 ? string.Join(", ", ports) : "(none)");
            return ports;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate available serial ports");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Connects using System.IO.Ports.SerialPort (more compatible with USB adapters).
    /// </summary>
    private async Task<ISerialConnection> ConnectWithSystemSerialPortAsync(SerialConnectionInfo info, CancellationToken ct)
    {
        var port = new SysSerialPort
        {
            PortName = info.PortName,
            BaudRate = info.BaudRate,
            DataBits = info.DataBits,
            Parity = (System.IO.Ports.Parity)(int)info.Parity,
            StopBits = (System.IO.Ports.StopBits)(int)info.StopBits,
            Handshake = (System.IO.Ports.Handshake)(int)info.Handshake,
            ReadTimeout = info.ReadTimeout,
            WriteTimeout = info.WriteTimeout,
            ReadBufferSize = info.ReadBufferSize,
            WriteBufferSize = info.WriteBufferSize
        };

        await Task.Run(() => port.Open(), ct);

        // Set DTR/RTS after opening
        try { port.DtrEnable = info.DtrEnable; }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to set DTR on {PortName}", info.PortName); }

        try
        {
            var handshake = (System.IO.Ports.Handshake)(int)info.Handshake;
            if (handshake != System.IO.Ports.Handshake.RequestToSend &&
                handshake != System.IO.Ports.Handshake.RequestToSendXOnXOff)
            {
                port.RtsEnable = info.RtsEnable;
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to set RTS on {PortName}", info.PortName); }

        if (!port.IsOpen)
        {
            port.Dispose();
            throw new InvalidOperationException($"Failed to open serial port {info.PortName}");
        }

        _logger.LogInformation("Serial port {PortName} opened with System.IO.Ports at {BaudRate} baud", info.PortName, info.BaudRate);
        return new SystemSerialConnection(port, _logger);
    }

    /// <summary>
    /// Validates the connection information parameters.
    /// </summary>
    /// <param name="info">The connection info to validate.</param>
    /// <exception cref="ArgumentNullException">If info is null.</exception>
    /// <exception cref="ArgumentException">If port name is invalid or settings are out of range.</exception>
    private void ValidateConnectionInfo(SerialConnectionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (string.IsNullOrWhiteSpace(info.PortName))
        {
            throw new ArgumentException("Port name cannot be null or empty.", nameof(info));
        }

        if (info.BaudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(info),
                info.BaudRate,
                "Baud rate must be greater than 0.");
        }

        if (info.DataBits < 5 || info.DataBits > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(info),
                info.DataBits,
                "Data bits must be between 5 and 8.");
        }
    }

    /// <summary>
    /// Logs detailed information about a connection failure.
    /// </summary>
    private void LogConnectionFailure(SerialConnectionInfo info, Exception ex, string? additionalContext)
    {
        var contextMessage = additionalContext != null ? $" ({additionalContext})" : "";

        _logger.LogError(
            "Failed to open serial port {PortName}{Context}: {Message}",
            info.PortName,
            contextMessage,
            ex.Message);

        _logger.LogDebug(
            "Serial port settings: BaudRate={BaudRate}, DataBits={DataBits}, Parity={Parity}, " +
            "StopBits={StopBits}, Handshake={Handshake}, DTR={Dtr}, RTS={Rts}",
            info.BaudRate,
            info.DataBits,
            info.Parity,
            info.StopBits,
            info.Handshake,
            info.DtrEnable,
            info.RtsEnable);

        _logger.LogDebug(ex, "Full exception details for serial port {PortName}", info.PortName);
    }

    /// <summary>
    /// Gets a single character representation of the parity setting.
    /// </summary>
    private static char GetParityChar(Parity parity)
    {
        return parity switch
        {
            Parity.None => 'N',
            Parity.Odd => 'O',
            Parity.Even => 'E',
            Parity.Mark => 'M',
            Parity.Space => 'S',
            _ => '?'
        };
    }

    /// <summary>
    /// Gets a string representation of the stop bits setting.
    /// </summary>
    private static string GetStopBitsString(StopBits stopBits)
    {
        return stopBits switch
        {
            StopBits.One => "1",
            StopBits.One5 => "1.5",
            StopBits.Two => "2",
            _ => "?"
        };
    }

    /// <summary>
    /// Attempts to set a serial port property, logging a warning if it fails.
    /// </summary>
    /// <param name="setAction">The action that sets the property.</param>
    /// <param name="propertyName">Name of the property for logging.</param>
    /// <param name="portName">Port name for logging.</param>
    private void TrySetProperty(Action setAction, string propertyName, string portName)
    {
        try
        {
            setAction();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to set {Property} on {PortName} - device may not support this setting",
                propertyName,
                portName);
        }
    }
}
