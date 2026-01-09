using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a reusable ProxyJump profile that defines a chain of jump hosts.
/// </summary>
public sealed class ProxyJumpProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-friendly display name for the profile.
    /// </summary>
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the profile.
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this profile is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Ordered list of jump hosts in this profile.
    /// </summary>
    public ICollection<ProxyJumpHop> JumpHops { get; set; } = new List<ProxyJumpHop>();

    /// <summary>
    /// Hosts that use this profile for their connections.
    /// </summary>
    public ICollection<HostEntry> AssociatedHosts { get; set; } = new List<HostEntry>();

    /// <summary>
    /// When this profile was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this profile was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
