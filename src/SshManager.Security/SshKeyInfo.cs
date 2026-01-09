namespace SshManager.Security;

/// <summary>
/// Information about an existing SSH key file.
/// </summary>
public sealed class SshKeyInfo
{
    /// <summary>
    /// Full path to the private key file.
    /// </summary>
    public string PrivateKeyPath { get; init; } = "";

    /// <summary>
    /// Full path to the public key file (if exists).
    /// </summary>
    public string? PublicKeyPath { get; init; }

    /// <summary>
    /// The key type (RSA, Ed25519, ECDSA, etc.).
    /// </summary>
    public SshKeyType? KeyType { get; init; }

    /// <summary>
    /// The key type as a string (for display when type is unknown).
    /// </summary>
    public string KeyTypeString { get; init; } = "Unknown";

    /// <summary>
    /// Key size in bits (for RSA/ECDSA).
    /// </summary>
    public int? KeySize { get; init; }

    /// <summary>
    /// SHA256 fingerprint of the public key.
    /// </summary>
    public string? Fingerprint { get; init; }

    /// <summary>
    /// Comment from the public key (usually email or identifier).
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Whether the private key is encrypted with a passphrase.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// File name of the private key (without path).
    /// </summary>
    public string FileName => Path.GetFileName(PrivateKeyPath);

    /// <summary>
    /// When the key file was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// When the key file was last modified.
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; init; }

    /// <summary>
    /// Display name for the key (file name or comment).
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(Comment) ? Comment : FileName;
}
