using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace SshManager.Core.Models;

/// <summary>
/// Represents an SSH host configuration.
/// </summary>
public sealed partial class HostEntry : IValidatableObject
{
    // Maximum lengths for string fields
    private const int MaxHostnameLength = 400;
    private const int MaxUsernameLength = 100;
    private const int MaxDisplayNameLength = 200;
    private const int MaxNotesLength = 5000;
    private const int MaxPathLength = 1000;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-friendly display name for the host.
    /// </summary>
    [StringLength(MaxDisplayNameLength, ErrorMessage = "Display name cannot exceed 200 characters")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Hostname or IP address.
    /// </summary>
    [Required(ErrorMessage = "Hostname is required")]
    [StringLength(MaxHostnameLength, ErrorMessage = "Hostname cannot exceed 400 characters")]
    public string Hostname { get; set; } = "";

    /// <summary>
    /// SSH port number (default: 22).
    /// </summary>
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
    public int Port { get; set; } = 22;

    /// <summary>
    /// SSH username.
    /// </summary>
    [StringLength(MaxUsernameLength, ErrorMessage = "Username cannot exceed 100 characters")]
    public string Username { get; set; } = "";

    /// <summary>
    /// Authentication method to use.
    /// </summary>
    public AuthType AuthType { get; set; } = AuthType.SshAgent;

    /// <summary>
    /// Path to private key file (for PrivateKeyFile auth type).
    /// </summary>
    [StringLength(MaxPathLength, ErrorMessage = "Private key path cannot exceed 1000 characters")]
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// DPAPI-encrypted password stored as base64 (for Password auth type).
    /// </summary>
    public string? PasswordProtected { get; set; }

    /// <summary>
    /// Optional notes about this host.
    /// </summary>
    [StringLength(MaxNotesLength, ErrorMessage = "Notes cannot exceed 5000 characters")]
    public string? Notes { get; set; }

    /// <summary>
    /// Optional group this host belongs to.
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Navigation property to the group.
    /// </summary>
    public HostGroup? Group { get; set; }

    /// <summary>
    /// Optional ProxyJump profile for connecting through jump hosts.
    /// </summary>
    public Guid? ProxyJumpProfileId { get; set; }

    /// <summary>
    /// Navigation property to the ProxyJump profile.
    /// </summary>
    public ProxyJumpProfile? ProxyJumpProfile { get; set; }

    /// <summary>
    /// Port forwarding profiles associated with this host.
    /// </summary>
    public ICollection<PortForwardingProfile> PortForwardingProfiles { get; set; }
        = new List<PortForwardingProfile>();

    /// <summary>
    /// When this host entry was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this host entry was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Validates the host entry beyond simple data annotations.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate hostname is not empty or whitespace
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            yield return new ValidationResult(
                "Hostname cannot be empty or whitespace",
                [nameof(Hostname)]);
        }
        else
        {
            // Validate hostname format (basic validation for hostname/IP)
            if (!IsValidHostname(Hostname) && !IsValidIpAddress(Hostname))
            {
                yield return new ValidationResult(
                    "Hostname must be a valid hostname or IP address",
                    [nameof(Hostname)]);
            }
        }

        // Validate username if provided
        if (!string.IsNullOrEmpty(Username) && Username.Contains('\0'))
        {
            yield return new ValidationResult(
                "Username contains invalid characters",
                [nameof(Username)]);
        }

        // Validate private key path when using PrivateKeyFile auth
        if (AuthType == AuthType.PrivateKeyFile)
        {
            if (string.IsNullOrWhiteSpace(PrivateKeyPath))
            {
                yield return new ValidationResult(
                    "Private key path is required when using PrivateKeyFile authentication",
                    [nameof(PrivateKeyPath)]);
            }
            else if (PrivateKeyPath.Contains(".."))
            {
                yield return new ValidationResult(
                    "Private key path cannot contain path traversal sequences",
                    [nameof(PrivateKeyPath)]);
            }
        }

        // Validate password is set when using Password auth
        if (AuthType == AuthType.Password && string.IsNullOrEmpty(PasswordProtected))
        {
            yield return new ValidationResult(
                "Password is required when using Password authentication",
                [nameof(PasswordProtected)]);
        }
    }

    /// <summary>
    /// Validates a hostname format (RFC 1123 compliant).
    /// </summary>
    private static bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 253)
            return false;

        // Hostname regex: labels separated by dots, each label 1-63 chars, alphanumeric or hyphen
        return HostnameRegex().IsMatch(hostname);
    }

    /// <summary>
    /// Validates an IP address format (IPv4 or IPv6).
    /// </summary>
    private static bool IsValidIpAddress(string address)
    {
        return System.Net.IPAddress.TryParse(address, out _);
    }

    /// <summary>
    /// Regex for validating hostnames per RFC 1123.
    /// </summary>
    [GeneratedRegex(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z0-9-]{1,63})*$", RegexOptions.Compiled)]
    private static partial Regex HostnameRegex();
}
