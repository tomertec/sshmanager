namespace SshManager.Core.Models;

/// <summary>
/// Types of connections supported by the application.
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// SSH connection over TCP/IP.
    /// </summary>
    Ssh = 0,

    /// <summary>
    /// Serial port connection (COM port).
    /// </summary>
    Serial = 1
}
