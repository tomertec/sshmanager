using System.IO;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Represents an active serial port connection.
/// </summary>
public interface ISerialConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The underlying stream for serial port I/O.
    /// </summary>
    Stream BaseStream { get; }

    /// <summary>
    /// Whether the connection is currently active and the port is open.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Whether the serial port is currently open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// The name of the serial port (e.g., "COM1", "COM3").
    /// </summary>
    string PortName { get; }

    /// <summary>
    /// The configured baud rate for the connection.
    /// </summary>
    int BaudRate { get; }

    /// <summary>
    /// Event raised when the connection is closed or an error occurs.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Sends a break signal on the serial port.
    /// </summary>
    /// <param name="duration">Duration of the break signal in milliseconds. Defaults to 250ms.</param>
    void SendBreak(int duration = 250);

    /// <summary>
    /// Sets the Data Terminal Ready (DTR) signal.
    /// </summary>
    /// <param name="enabled">True to enable DTR, false to disable.</param>
    void SetDtr(bool enabled);

    /// <summary>
    /// Sets the Request To Send (RTS) signal.
    /// </summary>
    /// <param name="enabled">True to enable RTS, false to disable.</param>
    void SetRts(bool enabled);
}

/// <summary>
/// Service for establishing serial port connections.
/// </summary>
public interface ISerialConnectionService
{
    /// <summary>
    /// Connects to a serial port and returns a connection for terminal I/O.
    /// </summary>
    /// <param name="info">The serial connection parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An active serial connection.</returns>
    /// <exception cref="ArgumentException">If the port name is invalid.</exception>
    /// <exception cref="InvalidOperationException">If the port cannot be opened.</exception>
    Task<ISerialConnection> ConnectAsync(SerialConnectionInfo info, CancellationToken ct = default);

    /// <summary>
    /// Gets the list of available serial ports on the system.
    /// </summary>
    /// <returns>Array of port names (e.g., ["COM1", "COM3"]).</returns>
    string[] GetAvailablePorts();
}
