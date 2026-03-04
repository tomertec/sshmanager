namespace SshManager.Core.Models;

/// <summary>
/// SSH authentication methods supported by the application.
/// </summary>
public enum AuthType
{
    /// <summary>
    /// Use SSH agent (Pageant, OpenSSH agent, etc.) for key-based authentication.
    /// </summary>
    SshAgent = 0,

    /// <summary>
    /// Use a private key file for authentication.
    /// </summary>
    PrivateKeyFile = 1,

    /// <summary>
    /// Use password authentication. Password is stored encrypted with DPAPI.
    /// </summary>
    Password = 2,

    /// <summary>
    /// Use Kerberos/GSSAPI authentication with Windows domain credentials.
    /// Supports enterprise SSO through Active Directory integration.
    /// </summary>
    Kerberos = 3,

    /// <summary>
    /// Use 1Password to fetch credentials at connection time via op:// secret references.
    /// Requires the 1Password CLI (op) and desktop app integration.
    /// </summary>
    OnePassword = 4
}
