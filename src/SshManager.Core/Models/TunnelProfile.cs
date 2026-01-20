using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a visual tunnel builder profile with nodes and edges forming a connection graph.
/// </summary>
public sealed class TunnelProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-friendly display name for the tunnel profile.
    /// </summary>
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the tunnel configuration.
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Collection of nodes in the tunnel graph.
    /// </summary>
    public ICollection<TunnelNode> Nodes { get; set; } = new List<TunnelNode>();

    /// <summary>
    /// Collection of edges connecting nodes in the tunnel graph.
    /// </summary>
    public ICollection<TunnelEdge> Edges { get; set; } = new List<TunnelEdge>();

    /// <summary>
    /// When this profile was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this profile was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
