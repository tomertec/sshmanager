using SshManager.Core.Models;

namespace SshManager.Terminal.Models;

/// <summary>
/// Connection parameters for establishing an SSH session.
/// </summary>
public sealed class TerminalConnectionInfo
{
    /// <summary>
    /// Hostname or IP address to connect to.
    /// </summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// SSH port number.
    /// </summary>
    public int Port { get; init; } = 22;

    /// <summary>
    /// SSH username.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Authentication method.
    /// </summary>
    public AuthType AuthType { get; init; }

    /// <summary>
    /// Decrypted password (for Password auth type).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Path to private key file (for PrivateKeyFile auth type).
    /// </summary>
    public string? PrivateKeyPath { get; init; }

    /// <summary>
    /// Passphrase for encrypted private key.
    /// </summary>
    public string? PrivateKeyPassphrase { get; init; }

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keep-alive interval (null or zero to disable).
    /// </summary>
    public TimeSpan? KeepAliveInterval { get; init; }

    /// <summary>
    /// The host entry ID for fingerprint storage (optional).
    /// </summary>
    public Guid? HostId { get; init; }

    /// <summary>
    /// Creates a connection info from a HostEntry and optional decrypted password.
    /// </summary>
    public static TerminalConnectionInfo FromHostEntry(
        HostEntry host,
        string? decryptedPassword = null,
        TimeSpan? timeout = null,
        TimeSpan? keepAliveInterval = null)
    {
        return new TerminalConnectionInfo
        {
            HostId = host.Id,
            Hostname = host.Hostname,
            Port = host.Port,
            Username = host.Username,
            AuthType = host.AuthType,
            Password = decryptedPassword,
            PrivateKeyPath = host.PrivateKeyPath,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
            KeepAliveInterval = keepAliveInterval
        };
    }
}
