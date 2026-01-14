using System.Text.RegularExpressions;

namespace SshManager.Core.Validation;

/// <summary>
/// Centralized validation patterns used throughout the application.
/// Uses source-generated regex for optimal performance and AOT compatibility.
/// </summary>
public static partial class ValidationPatterns
{
    /// <summary>
    /// Validates hostname format per RFC 1123.
    /// Labels separated by dots, each 1-63 characters, alphanumeric or hyphen.
    /// Cannot start or end with hyphen.
    /// Maximum total length: 253 characters.
    /// </summary>
    /// <example>
    /// Valid: "server1", "my-host.example.com", "192-168-1-1.example.com"
    /// Invalid: "-invalid", "invalid-", "host..name"
    /// </example>
    [GeneratedRegex(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z0-9-]{1,63})*$", RegexOptions.Compiled)]
    public static partial Regex HostnameRegex();

    /// <summary>
    /// Validates IPv4 address format (basic pattern, does not validate octet ranges).
    /// For full validation, use <see cref="IsValidIpv4Address"/> method.
    /// </summary>
    [GeneratedRegex(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled)]
    public static partial Regex Ipv4AddressRegex();

    /// <summary>
    /// Validates Unix username format.
    /// Must start with letter or underscore, followed by alphanumeric, underscore, hyphen, or period.
    /// </summary>
    /// <example>
    /// Valid: "admin", "user_name", "_service", "deploy.user"
    /// Invalid: "1user", "-user", ".user"
    /// </example>
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_\-\.]*$", RegexOptions.Compiled)]
    public static partial Regex UsernameRegex();

    /// <summary>
    /// Validates bind address format for port forwarding.
    /// Accepts IPv4, localhost, wildcard (*), and IPv6 loopback.
    /// </summary>
    [GeneratedRegex(@"^(?:\d{1,3}\.){3}\d{1,3}$|^localhost$|^\*$|^::1?$|^0\.0\.0\.0$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex BindAddressRegex();

    /// <summary>
    /// Maximum allowed hostname length per RFC 1035.
    /// </summary>
    public const int MaxHostnameLength = 253;

    /// <summary>
    /// Validates if a string is a valid hostname.
    /// </summary>
    /// <param name="hostname">The hostname to validate.</param>
    /// <returns>True if valid hostname format, false otherwise.</returns>
    public static bool IsValidHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return false;
        if (hostname.Length > MaxHostnameLength)
            return false;
        return HostnameRegex().IsMatch(hostname);
    }

    /// <summary>
    /// Validates if a string is a valid IPv4 address with proper octet ranges (0-255).
    /// </summary>
    /// <param name="ip">The IP address to validate.</param>
    /// <returns>True if valid IPv4 address, false otherwise.</returns>
    public static bool IsValidIpv4Address(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return false;
        if (!Ipv4AddressRegex().IsMatch(ip))
            return false;

        var parts = ip.Split('.');
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var octet) || octet < 0 || octet > 255)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Validates if a string is a valid hostname or IPv4 address.
    /// </summary>
    /// <param name="hostOrIp">The host or IP to validate.</param>
    /// <returns>True if valid hostname or IPv4 address, false otherwise.</returns>
    public static bool IsValidHostOrIpAddress(string? hostOrIp)
    {
        return IsValidHostname(hostOrIp) || IsValidIpv4Address(hostOrIp);
    }

    /// <summary>
    /// Validates if a string is a valid Unix username.
    /// </summary>
    /// <param name="username">The username to validate.</param>
    /// <returns>True if valid username format, false otherwise.</returns>
    public static bool IsValidUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return false;
        return UsernameRegex().IsMatch(username);
    }

    /// <summary>
    /// Validates if a string is a valid port number.
    /// </summary>
    /// <param name="port">The port number to validate.</param>
    /// <returns>True if valid port (1-65535), false otherwise.</returns>
    public static bool IsValidPort(int port)
    {
        return port >= 1 && port <= 65535;
    }

    /// <summary>
    /// Validates if a string represents a valid port number.
    /// </summary>
    /// <param name="portString">The port string to validate.</param>
    /// <returns>True if valid port number string, false otherwise.</returns>
    public static bool IsValidPort(string? portString)
    {
        if (string.IsNullOrWhiteSpace(portString))
            return false;
        return int.TryParse(portString, out var port) && IsValidPort(port);
    }

    /// <summary>
    /// Validates if a string is a valid bind address for port forwarding.
    /// </summary>
    /// <param name="bindAddress">The bind address to validate.</param>
    /// <returns>True if valid bind address, false otherwise.</returns>
    public static bool IsValidBindAddress(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
            return false;
        return BindAddressRegex().IsMatch(bindAddress);
    }
}
