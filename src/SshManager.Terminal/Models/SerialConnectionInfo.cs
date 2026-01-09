using RJCP.IO.Ports;
using SshManager.Core.Models;

namespace SshManager.Terminal.Models;

/// <summary>
/// Connection parameters for establishing a serial port session.
/// </summary>
/// <remarks>
/// <para>
/// This class encapsulates all settings needed to configure a serial port connection,
/// following the same pattern as <see cref="TerminalConnectionInfo"/> for SSH.
/// </para>
/// <para>
/// Default values are set to common serial terminal settings (9600 8N1), which are
/// widely compatible with embedded devices and console connections.
/// </para>
/// </remarks>
public sealed class SerialConnectionInfo
{
    /// <summary>
    /// Serial port name (e.g., "COM1", "COM3" on Windows, "/dev/ttyUSB0" on Linux).
    /// </summary>
    public string PortName { get; init; } = "COM1";

    /// <summary>
    /// Baud rate for serial communication.
    /// Common values: 9600, 19200, 38400, 57600, 115200.
    /// </summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>
    /// Number of data bits per byte (typically 7 or 8).
    /// </summary>
    public int DataBits { get; init; } = 8;

    /// <summary>
    /// Number of stop bits.
    /// </summary>
    public StopBits StopBits { get; init; } = StopBits.One;

    /// <summary>
    /// Parity checking protocol.
    /// </summary>
    public Parity Parity { get; init; } = Parity.None;

    /// <summary>
    /// Handshaking protocol for serial port transmission.
    /// </summary>
    public Handshake Handshake { get; init; } = Handshake.None;

    /// <summary>
    /// Whether to enable Data Terminal Ready (DTR) signal.
    /// </summary>
    public bool DtrEnable { get; init; } = true;

    /// <summary>
    /// Whether to enable Request to Send (RTS) signal.
    /// </summary>
    public bool RtsEnable { get; init; } = true;

    /// <summary>
    /// Read timeout in milliseconds. Set to -1 for infinite timeout.
    /// </summary>
    public int ReadTimeout { get; init; } = -1;

    /// <summary>
    /// Write timeout in milliseconds. Set to -1 for infinite timeout.
    /// </summary>
    public int WriteTimeout { get; init; } = -1;

    /// <summary>
    /// Size of the receive buffer in bytes.
    /// </summary>
    public int ReadBufferSize { get; init; } = 4096;

    /// <summary>
    /// Size of the transmit buffer in bytes.
    /// </summary>
    public int WriteBufferSize { get; init; } = 2048;

    /// <summary>
    /// Whether to echo typed characters locally.
    /// </summary>
    public bool LocalEcho { get; init; } = false;

    /// <summary>
    /// Line ending sequence to append when sending data.
    /// Common values: "\r\n" (Windows/CRLF), "\n" (Unix/LF), "\r" (Mac/CR).
    /// </summary>
    public string LineEnding { get; init; } = "\r\n";

    /// <summary>
    /// Gets a display-friendly description of the connection settings.
    /// </summary>
    /// <returns>A string like "COM3 9600 8N1".</returns>
    public string GetDisplayString()
    {
        var parityChar = Parity switch
        {
            Parity.None => 'N',
            Parity.Odd => 'O',
            Parity.Even => 'E',
            Parity.Mark => 'M',
            Parity.Space => 'S',
            _ => '?'
        };

        var stopBitsStr = StopBits switch
        {
            StopBits.One => "1",
            StopBits.One5 => "1.5",
            StopBits.Two => "2",
            _ => "?"
        };

        return $"{PortName} {BaudRate} {DataBits}{parityChar}{stopBitsStr}";
    }

    /// <summary>
    /// Creates a SerialConnectionInfo from a HostEntry.
    /// </summary>
    /// <param name="host">The host entry to create connection info from.</param>
    /// <returns>A new SerialConnectionInfo populated from the host's serial settings.</returns>
    /// <remarks>
    /// Enum values are cast from System.IO.Ports to RJCP.IO.Ports as they share the same underlying values.
    /// </remarks>
    public static SerialConnectionInfo FromHostEntry(HostEntry host) => new()
    {
        PortName = host.SerialPortName ?? "COM1",
        BaudRate = host.SerialBaudRate,
        DataBits = host.SerialDataBits,
        StopBits = (StopBits)(int)host.SerialStopBits,
        Parity = (Parity)(int)host.SerialParity,
        Handshake = (Handshake)(int)host.SerialHandshake,
        DtrEnable = host.SerialDtrEnable,
        RtsEnable = host.SerialRtsEnable,
        LocalEcho = host.SerialLocalEcho,
        LineEnding = host.SerialLineEnding
    };
}
