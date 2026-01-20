namespace SshManager.Core.Exceptions;

/// <summary>
/// Exception thrown when a serial port connection fails.
/// Provides structured information about the failure reason and port details.
/// </summary>
public class SerialConnectionException : SshManagerException
{
    /// <summary>
    /// Gets the categorized reason for the connection failure.
    /// </summary>
    public ConnectionFailedReason Reason { get; }

    /// <summary>
    /// Gets the serial port name (e.g., "COM3").
    /// </summary>
    public string? PortName { get; }

    /// <summary>
    /// Gets the baud rate that was configured.
    /// </summary>
    public int? BaudRate { get; }

    /// <summary>
    /// Creates a new SerialConnectionException.
    /// </summary>
    /// <param name="reason">The categorized reason for failure.</param>
    /// <param name="portName">The serial port name.</param>
    /// <param name="baudRate">The configured baud rate.</param>
    /// <param name="message">Technical error message.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public SerialConnectionException(
        ConnectionFailedReason reason,
        string? portName = null,
        int? baudRate = null,
        string? message = null,
        Exception? innerException = null)
        : base(
            message ?? GetDefaultMessage(reason, portName),
            GetUserFriendlyMessage(reason, portName),
            GetErrorCode(reason),
            innerException)
    {
        Reason = reason;
        PortName = portName;
        BaudRate = baudRate;
    }

    private static string GetDefaultMessage(ConnectionFailedReason reason, string? portName)
    {
        var port = portName ?? "serial port";

        return reason switch
        {
            ConnectionFailedReason.DeviceNotFound => $"Device not found: {port}",
            ConnectionFailedReason.PortNotAvailable => $"Port {port} is not available or in use",
            ConnectionFailedReason.PermissionDenied => $"Access denied to {port}",
            ConnectionFailedReason.InvalidConfiguration => $"Invalid configuration for {port}",
            ConnectionFailedReason.ConnectionTimedOut => $"Connection to {port} timed out",
            ConnectionFailedReason.ServerDisconnected => $"Device on {port} disconnected",
            _ => $"Failed to connect to {port}"
        };
    }

    private static string GetUserFriendlyMessage(ConnectionFailedReason reason, string? portName)
    {
        var port = portName ?? "the serial port";

        return reason switch
        {
            ConnectionFailedReason.DeviceNotFound =>
                $"Serial port {port} was not found. Check that the device is connected and the port name is correct.",
            ConnectionFailedReason.PortNotAvailable =>
                $"Port {port} is not available. It may be in use by another application.",
            ConnectionFailedReason.PermissionDenied =>
                $"Access denied to {port}. You may need administrator privileges.",
            ConnectionFailedReason.InvalidConfiguration =>
                $"The configuration for {port} is invalid. Check the baud rate and other serial settings.",
            ConnectionFailedReason.ConnectionTimedOut =>
                $"Connection to {port} timed out. Check that the device is powered on and connected.",
            ConnectionFailedReason.ServerDisconnected =>
                $"The device on {port} disconnected unexpectedly.",
            _ => $"Failed to connect to {port}. Check the connection and settings."
        };
    }

    private static string GetErrorCode(ConnectionFailedReason reason)
    {
        return $"SERIAL_{reason.ToString().ToUpperInvariant()}";
    }
}
