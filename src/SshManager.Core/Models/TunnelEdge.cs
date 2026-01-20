namespace SshManager.Core.Models;

/// <summary>
/// Represents a connection/edge between two nodes in the tunnel builder graph.
/// </summary>
public sealed class TunnelEdge
{
    /// <summary>
    /// Unique identifier for this edge.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The ID of the source node.
    /// </summary>
    public Guid SourceNodeId { get; set; }

    /// <summary>
    /// The ID of the target node.
    /// </summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// The ID of the profile this edge belongs to.
    /// </summary>
    public Guid TunnelProfileId { get; set; }

    /// <summary>
    /// Navigation property to the parent profile.
    /// </summary>
    public TunnelProfile? TunnelProfile { get; set; }

    /// <summary>
    /// Navigation property to the source node.
    /// </summary>
    public TunnelNode? SourceNode { get; set; }

    /// <summary>
    /// Navigation property to the target node.
    /// </summary>
    public TunnelNode? TargetNode { get; set; }
}
