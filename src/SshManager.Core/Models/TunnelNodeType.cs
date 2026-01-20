namespace SshManager.Core.Models;

/// <summary>
/// Represents the type of node in a visual SSH tunnel builder.
/// </summary>
public enum TunnelNodeType
{
    /// <summary>
    /// The local machine (starting point for tunnels).
    /// </summary>
    LocalMachine,

    /// <summary>
    /// An SSH host that can be connected to.
    /// </summary>
    SshHost,

    /// <summary>
    /// A local port that will be forwarded.
    /// </summary>
    LocalPort,

    /// <summary>
    /// A remote port on a target host.
    /// </summary>
    RemotePort,

    /// <summary>
    /// A dynamic SOCKS proxy.
    /// </summary>
    DynamicProxy,

    /// <summary>
    /// A target host (destination for port forwards).
    /// </summary>
    TargetHost
}
