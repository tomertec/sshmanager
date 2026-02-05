using System.IO.Ports;

namespace SshManager.Core.Models;

/// <summary>
/// Configuration settings for serial port connections.
/// </summary>
public sealed class SerialPortSettings
{
    /// <summary>
    /// Serial port name (e.g., "COM1", "COM3").
    /// </summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>
    /// Baud rate for serial communication.
    /// Common values: 9600, 19200, 38400, 57600, 115200.
    /// </summary>
    public int BaudRate { get; set; } = Constants.SerialDefaults.DefaultBaudRate;

    /// <summary>
    /// Number of data bits per byte (typically 7 or 8).
    /// </summary>
    public int DataBits { get; set; } = Constants.SerialDefaults.DefaultDataBits;

    /// <summary>
    /// Number of stop bits used for each byte.
    /// </summary>
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>
    /// Parity checking protocol.
    /// </summary>
    public Parity Parity { get; set; } = Parity.None;

    /// <summary>
    /// Hardware or software handshaking protocol.
    /// </summary>
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>
    /// Whether to enable Data Terminal Ready (DTR) signal.
    /// </summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>
    /// Whether to enable Request To Send (RTS) signal.
    /// </summary>
    public bool RtsEnable { get; set; } = true;

    /// <summary>
    /// Read timeout in milliseconds. -1 for infinite timeout.
    /// </summary>
    public int ReadTimeout { get; set; } = 500;

    /// <summary>
    /// Write timeout in milliseconds. -1 for infinite timeout.
    /// </summary>
    public int WriteTimeout { get; set; } = 500;

    /// <summary>
    /// Whether to echo typed characters locally.
    /// </summary>
    public bool LocalEcho { get; set; } = false;

    /// <summary>
    /// Line ending characters to send when Enter is pressed.
    /// Common values: "\r\n" (Windows), "\n" (Unix), "\r" (Mac classic).
    /// </summary>
    public string LineEnding { get; set; } = Constants.SerialDefaults.DefaultLineEnding;
}
