using System.ComponentModel.DataAnnotations;
using System.IO.Ports;
using SshManager.Core.Validation;

namespace SshManager.Core.Models;

/// <summary>
/// Represents an SSH host configuration.
/// </summary>
public sealed partial class HostEntry : IValidatableObject
{
    // Maximum lengths for string fields - using shared constants
    private const int MaxHostnameLength = Constants.StringLimits.MaxHostnameLength;
    private const int MaxUsernameLength = Constants.StringLimits.MaxUsernameLength;
    private const int MaxDisplayNameLength = Constants.StringLimits.MaxDisplayNameLength;
    private const int MaxNotesLength = Constants.StringLimits.MaxNotesLength;
    private const int MaxPathLength = Constants.StringLimits.MaxPathLength;

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
    [Range(Constants.Network.MinPort, Constants.Network.MaxPort, ErrorMessage = "Port must be between 1 and 65535")]
    public int Port { get; set; } = Constants.Network.DefaultSshPort;

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
    /// Kerberos service principal name for GSSAPI authentication (for Kerberos auth type).
    /// Example: "host/server.domain.com" or null to use default principal.
    /// </summary>
    [StringLength(MaxHostnameLength, ErrorMessage = "Kerberos service principal cannot exceed 400 characters")]
    public string? KerberosServicePrincipal { get; set; }

    /// <summary>
    /// Enable Kerberos credential delegation (ticket forwarding) for this host.
    /// When enabled, allows the remote host to act on your behalf.
    /// </summary>
    public bool KerberosDelegateCredentials { get; set; }

    /// <summary>
    /// Optional notes about this host.
    /// </summary>
    [StringLength(MaxNotesLength, ErrorMessage = "Notes cannot exceed 5000 characters")]
    public string? Notes { get; set; }

    /// <summary>
    /// DPAPI-encrypted secure notes (base64 encoded). Use for sensitive information like API keys.
    /// </summary>
    [MaxLength(Constants.StringLimits.MaxSecureNotesLength)]
    public string? SecureNotesProtected { get; set; }

    /// <summary>
    /// Connection type (SSH or Serial).
    /// </summary>
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Ssh;

    /// <summary>
    /// The type of shell on the remote host for environment variable handling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This setting determines how environment variables are applied to the remote session.
    /// POSIX-compliant shells (bash, zsh, sh) use <c>export VAR="value"</c> syntax.
    /// </para>
    /// <para>
    /// For non-POSIX shells (PowerShell, CMD, network appliances), environment variables
    /// are skipped to avoid command errors or unexpected behavior.
    /// </para>
    /// </remarks>
    public ShellType ShellType { get; set; } = ShellType.Auto;

    /// <summary>
    /// Serial port name (e.g., "COM1", "COM3").
    /// </summary>
    public string? SerialPortName { get; set; }

    /// <summary>
    /// Serial port baud rate (default: 9600).
    /// </summary>
    public int SerialBaudRate { get; set; } = Constants.SerialDefaults.DefaultBaudRate;

    /// <summary>
    /// Serial port data bits (default: 8).
    /// </summary>
    public int SerialDataBits { get; set; } = Constants.SerialDefaults.DefaultDataBits;

    /// <summary>
    /// Serial port stop bits (default: One).
    /// </summary>
    public StopBits SerialStopBits { get; set; } = StopBits.One;

    /// <summary>
    /// Serial port parity (default: None).
    /// </summary>
    public Parity SerialParity { get; set; } = Parity.None;

    /// <summary>
    /// Serial port handshake mode (default: None).
    /// </summary>
    public Handshake SerialHandshake { get; set; } = Handshake.None;

    /// <summary>
    /// Enable DTR (Data Terminal Ready) signal (default: true).
    /// </summary>
    public bool SerialDtrEnable { get; set; } = true;

    /// <summary>
    /// Enable RTS (Request To Send) signal (default: true).
    /// </summary>
    public bool SerialRtsEnable { get; set; } = true;

    /// <summary>
    /// Enable local echo for serial connections (default: false).
    /// </summary>
    public bool SerialLocalEcho { get; set; } = false;

    /// <summary>
    /// Line ending to use when sending data over serial (default: "\r\n").
    /// </summary>
    public string SerialLineEnding { get; set; } = Constants.SerialDefaults.DefaultLineEnding;

    /// <summary>
    /// Per-host keep-alive interval in seconds.
    /// Null = use global setting, 0 = disable, >0 = specific interval.
    /// </summary>
    [Range(0, 3600)]
    public int? KeepAliveIntervalSeconds { get; set; }

    // ===== X11 Forwarding Settings =====

    /// <summary>
    /// Whether X11 forwarding is enabled for this host.
    /// Null means use global default from AppSettings.
    /// </summary>
    public bool? X11ForwardingEnabled { get; set; }

    /// <summary>
    /// Whether to use trusted X11 forwarding (-Y instead of -X).
    /// Trusted forwarding allows more access but is less secure.
    /// </summary>
    public bool X11TrustedForwarding { get; set; }

    /// <summary>
    /// X11 display number to use. Default is 0 (:0).
    /// </summary>
    public int? X11DisplayNumber { get; set; }

    /// <summary>
    /// Optional group this host belongs to.
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Navigation property to the group.
    /// </summary>
    public HostGroup? Group { get; set; }

    /// <summary>
    /// Optional host profile that provides default settings for this host.
    /// </summary>
    public Guid? HostProfileId { get; set; }

    /// <summary>
    /// Navigation property to the host profile.
    /// </summary>
    public HostProfile? HostProfile { get; set; }

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
    /// Tags associated with this host for categorization and filtering.
    /// </summary>
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();

    /// <summary>
    /// Environment variables to be set on SSH connection.
    /// </summary>
    public ICollection<HostEnvironmentVariable> EnvironmentVariables { get; set; }
        = new List<HostEnvironmentVariable>();

    /// <summary>
    /// Sort order for display within a group (lower numbers appear first).
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether this host is marked as a favorite for quick access.
    /// </summary>
    public bool IsFavorite { get; set; }

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
            if (!ValidationPatterns.IsValidHostOrIpAddress(Hostname))
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
            else if (!ValidationPatterns.IsPathTraversalSafe(PrivateKeyPath))
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

}
