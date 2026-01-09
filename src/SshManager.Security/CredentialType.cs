namespace SshManager.Security;

/// <summary>
/// Types of credentials that can be cached.
/// </summary>
public enum CredentialType
{
    /// <summary>
    /// A plain password for password-based authentication.
    /// </summary>
    Password = 0,

    /// <summary>
    /// A passphrase for an encrypted private key file.
    /// </summary>
    KeyPassphrase = 1
}
