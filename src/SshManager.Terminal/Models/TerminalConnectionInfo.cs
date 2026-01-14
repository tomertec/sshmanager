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
    /// When true, explicitly skips host key verification.
    /// Default is false, meaning host key verification is expected.
    /// </summary>
    /// <remarks>
    /// <para><strong>SECURITY WARNING:</strong> Setting this to <c>true</c> disables host key verification,
    /// which protects against man-in-the-middle (MITM) attacks. Only set this to <c>true</c> if you
    /// understand the security implications and have other means of verifying the server's identity.</para>
    ///
    /// <para>When this is <c>false</c> (the default) and no host key verification callback is provided
    /// to the connection service, a warning will be logged to alert about the potential security risk.</para>
    /// </remarks>
    public bool SkipHostKeyVerification { get; init; } = false;

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
