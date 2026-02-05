using SshManager.Core.Models;

namespace SshManager.App.Services.Validation;

/// <summary>
/// Service for validating SSH and Serial connection host entries.
/// </summary>
public interface IHostValidationService
{
    /// <summary>
    /// Validates SSH connection parameters.
    /// </summary>
    /// <param name="hostname">The hostname or IP address.</param>
    /// <param name="port">The port number (1-65535).</param>
    /// <param name="username">The username for authentication.</param>
    /// <param name="authType">The authentication type.</param>
    /// <param name="privateKeyPath">The path to the private key file (required for PrivateKeyFile auth).</param>
    /// <param name="password">The password (required for Password auth).</param>
    /// <returns>A list of validation error messages. Empty list if validation passes.</returns>
    List<string> ValidateSshConnection(string hostname, int port, string username,
        AuthType authType, string? privateKeyPath, string? password);

    /// <summary>
    /// Validates serial port connection parameters.
    /// </summary>
    /// <param name="portName">The COM port name (e.g., "COM1").</param>
    /// <param name="baudRate">The baud rate (must be positive).</param>
    /// <param name="dataBits">The data bits (5-8).</param>
    /// <returns>A list of validation error messages. Empty list if validation passes.</returns>
    List<string> ValidateSerialConnection(string? portName, int baudRate, int dataBits);

    /// <summary>
    /// Validates whether the given string is a valid hostname.
    /// </summary>
    /// <param name="hostname">The hostname to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool IsValidHostname(string hostname);

    /// <summary>
    /// Validates whether the given string is a valid IPv4 address.
    /// </summary>
    /// <param name="ip">The IP address to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool IsValidIpAddress(string ip);
}
