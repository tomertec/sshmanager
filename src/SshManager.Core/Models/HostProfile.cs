using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a reusable profile with common settings that can be applied to multiple SSH hosts.
/// </summary>
public sealed class HostProfile
{
    private const int MaxDisplayNameLength = Constants.StringLimits.MaxDisplayNameLength;
    private const int MaxDescriptionLength = Constants.StringLimits.MaxPathLength;
    private const int MaxPathLength = Constants.StringLimits.MaxPathLength;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the profile.
    /// </summary>
    [Required(ErrorMessage = "Display name is required")]
    [StringLength(MaxDisplayNameLength, ErrorMessage = "Display name cannot exceed 200 characters")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Optional description of what this profile is for.
    /// </summary>
    [StringLength(MaxDescriptionLength, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    /// <summary>
    /// Default SSH port number (default: 22).
    /// </summary>
    [Range(Constants.Network.MinPort, Constants.Network.MaxPort, ErrorMessage = "Port must be between 1 and 65535")]
    public int DefaultPort { get; set; } = Constants.Network.DefaultSshPort;

    /// <summary>
    /// Default username to use for connections.
    /// </summary>
    [StringLength(Constants.StringLimits.MaxUsernameLength, ErrorMessage = "Username cannot exceed 100 characters")]
    public string? DefaultUsername { get; set; }

    /// <summary>
    /// Default authentication method to use.
    /// </summary>
    public AuthType AuthType { get; set; } = AuthType.SshAgent;

    /// <summary>
    /// Path to private key file (for PrivateKeyFile auth type).
    /// </summary>
    [StringLength(MaxPathLength, ErrorMessage = "Private key path cannot exceed 1000 characters")]
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Optional ProxyJump profile to use for connections with this profile.
    /// </summary>
    public Guid? ProxyJumpProfileId { get; set; }

    /// <summary>
    /// Navigation property to the ProxyJump profile.
    /// </summary>
    public ProxyJumpProfile? ProxyJumpProfile { get; set; }

    /// <summary>
    /// Hosts that use this profile.
    /// </summary>
    public ICollection<HostEntry> Hosts { get; set; } = new List<HostEntry>();

    /// <summary>
    /// When this profile was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this profile was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
