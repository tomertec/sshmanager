namespace SshManager.Core.Models;

/// <summary>
/// Represents a node in the visual SSH tunnel builder graph.
/// </summary>
public sealed class TunnelNode
{
    /// <summary>
    /// Unique identifier for this node.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The type of this node.
    /// </summary>
    public TunnelNodeType NodeType { get; set; }

    /// <summary>
    /// The HostEntry ID if this node represents an SSH host.
    /// </summary>
    public Guid? HostId { get; set; }

    /// <summary>
    /// User-defined label for this node.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// X position on the canvas for visual representation.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y position on the canvas for visual representation.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Local port number.
    /// For LocalPort: the port on the local machine to listen on.
    /// For RemotePort: the target port on the forwarding destination (confusingly named, but represents the target port).
    /// For DynamicProxy: the local SOCKS proxy port to listen on.
    /// </summary>
    public int? LocalPort { get; set; }

    /// <summary>
    /// Remote port number (for RemotePort node type).
    /// </summary>
    public int? RemotePort { get; set; }

    /// <summary>
    /// Remote hostname (for TargetHost and RemotePort node types).
    /// For RemotePort: the target host where forwarded connections will be sent to.
    /// For TargetHost: the destination hostname for the port forward.
    /// </summary>
    public string? RemoteHost { get; set; }

    /// <summary>
    /// Bind address for port forwarding.
    /// For LocalPort: the interface on the local machine to bind to (default: "localhost").
    /// For RemotePort: the interface on the remote server to bind to (default: loopback).
    /// </summary>
    public string? BindAddress { get; set; }

    /// <summary>
    /// The ID of the profile this node belongs to.
    /// </summary>
    public Guid TunnelProfileId { get; set; }

    /// <summary>
    /// Navigation property to the parent profile.
    /// </summary>
    public TunnelProfile? TunnelProfile { get; set; }
}
