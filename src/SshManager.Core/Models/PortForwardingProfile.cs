using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a port forwarding configuration that can be associated with a host.
/// </summary>
public sealed class PortForwardingProfile
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
    /// The type of port forwarding.
    /// </summary>
    public PortForwardingType ForwardingType { get; set; }

    /// <summary>
    /// Local bind address (default: 127.0.0.1).
    /// </summary>
    [StringLength(400)]
    public string LocalBindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// Local port to bind.
    /// </summary>
    [Range(1, 65535)]
    public int LocalPort { get; set; }

    /// <summary>
    /// Remote host for the forwarding (not used for DynamicForward).
    /// </summary>
    [StringLength(400)]
    public string? RemoteHost { get; set; }

    /// <summary>
    /// Remote port for the forwarding (not used for DynamicForward).
    /// </summary>
    [Range(1, 65535)]
    public int? RemotePort { get; set; }

    /// <summary>
    /// Whether this profile is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether to automatically start this forwarding when connecting.
    /// </summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>
    /// Optional host association. If null, this is a global profile.
    /// </summary>
    public Guid? HostId { get; set; }

    /// <summary>
    /// Navigation property to the associated host.
    /// </summary>
    public HostEntry? Host { get; set; }

    /// <summary>
    /// When this profile was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this profile was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
