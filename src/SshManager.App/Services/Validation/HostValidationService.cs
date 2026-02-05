using System.IO;
using System.Text.RegularExpressions;
using SshManager.Core.Models;

namespace SshManager.App.Services.Validation;

/// <summary>
/// Implementation of host validation service for SSH and Serial connections.
/// </summary>
public class HostValidationService : IHostValidationService
{
    // Validation regex patterns (extracted from HostDialogViewModel)
    private static readonly Regex HostnameRegex = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_\-\.]*$", RegexOptions.Compiled);

    /// <inheritdoc />
    public List<string> ValidateSshConnection(string hostname, int port, string username,
        AuthType authType, string? privateKeyPath, string? password)
    {
        var errors = new List<string>();

        // Hostname validation
        var trimmedHostname = hostname?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmedHostname))
        {
            errors.Add("Hostname is required");
        }
        else if (!IsValidHostname(trimmedHostname) && !IsValidIpAddress(trimmedHostname))
        {
            errors.Add("Invalid hostname or IP address format");
        }

        // Port validation
        if (port < 1 || port > 65535)
        {
            errors.Add("Port must be between 1 and 65535");
        }

        // Username validation
        var trimmedUsername = username?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmedUsername))
        {
            errors.Add("Username is required");
        }
        else if (trimmedUsername.Length > 32)
        {
            errors.Add("Username must be 32 characters or less");
        }
        else if (!UsernameRegex.IsMatch(trimmedUsername))
        {
            errors.Add("Username contains invalid characters");
        }

        // Auth-type specific validation
        switch (authType)
        {
            case AuthType.Password:
                if (string.IsNullOrEmpty(password))
                {
                    errors.Add("Password is required for password authentication");
                }
                break;

            case AuthType.PrivateKeyFile:
                var keyPath = privateKeyPath?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(keyPath))
                {
                    errors.Add("Private key file path is required");
                }
                else if (!File.Exists(keyPath))
                {
                    errors.Add($"Private key file not found: {keyPath}");
                }
                break;
        }

        return errors;
    }

    /// <inheritdoc />
    public List<string> ValidateSerialConnection(string? portName, int baudRate, int dataBits)
    {
        var errors = new List<string>();

        // Serial port validation
        if (string.IsNullOrWhiteSpace(portName))
        {
            errors.Add("COM Port is required");
        }

        // Validate baud rate
        if (baudRate <= 0)
        {
            errors.Add("Baud rate must be a positive number");
        }

        // Validate data bits
        if (dataBits < 5 || dataBits > 8)
        {
            errors.Add("Data bits must be between 5 and 8");
        }

        return errors;
    }

    /// <inheritdoc />
    public bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrEmpty(hostname) || hostname.Length > 253)
        {
            return false;
        }
        return HostnameRegex.IsMatch(hostname);
    }

    /// <inheritdoc />
    public bool IsValidIpAddress(string ip)
    {
        if (string.IsNullOrEmpty(ip) || !IpAddressRegex.IsMatch(ip))
        {
            return false;
        }

        var parts = ip.Split('.');
        return parts.All(p => int.TryParse(p, out var num) && num >= 0 && num <= 255);
    }
}
