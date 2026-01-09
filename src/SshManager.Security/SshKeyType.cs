namespace SshManager.Security;

/// <summary>
/// Supported SSH key types for generation.
/// </summary>
public enum SshKeyType
{
    /// <summary>
    /// RSA 2048-bit key.
    /// </summary>
    Rsa2048,

    /// <summary>
    /// RSA 4096-bit key (more secure, slower).
    /// </summary>
    Rsa4096,

    /// <summary>
    /// Ed25519 key (recommended, modern, fast).
    /// </summary>
    Ed25519,

    /// <summary>
    /// ECDSA 256-bit key (NIST P-256).
    /// </summary>
    Ecdsa256,

    /// <summary>
    /// ECDSA 384-bit key (NIST P-384).
    /// </summary>
    Ecdsa384,

    /// <summary>
    /// ECDSA 521-bit key (NIST P-521).
    /// </summary>
    Ecdsa521
}
