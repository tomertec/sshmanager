namespace SshManager.Core.Models;

/// <summary>
/// Types of SSH port forwarding.
/// </summary>
public enum PortForwardingType
{
    /// <summary>
    /// Local port forwarding: -L [bind_address:]port:host:hostport
    /// Traffic to local port is forwarded to remote host:port through SSH tunnel.
    /// </summary>
    LocalForward = 0,

    /// <summary>
    /// Remote port forwarding: -R [bind_address:]port:host:hostport
    /// Traffic to remote port is forwarded back to local host:port.
    /// </summary>
    RemoteForward = 1,

    /// <summary>
    /// Dynamic port forwarding (SOCKS5 proxy): -D [bind_address:]port
    /// Creates a SOCKS5 proxy on local port.
    /// </summary>
    DynamicForward = 2
}
