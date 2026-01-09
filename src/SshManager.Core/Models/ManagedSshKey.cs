namespace SshManager.Core.Models;

/// <summary>
/// Represents an SSH key tracked by the application.
/// </summary>
public sealed class ManagedSshKey
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-friendly display name for the key.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Full path to the private key file.
    /// </summary>
    public string PrivateKeyPath { get; set; } = "";

    /// <summary>
    /// The type of key (RSA, Ed25519, ECDSA, etc.).
    /// </summary>
    public int KeyType { get; set; }

    /// <summary>
    /// SHA256 fingerprint of the public key.
    /// </summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>
    /// Optional comment embedded in the key.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Whether the private key is encrypted with a passphrase.
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// When this key was added to the application.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this key was last used for a connection.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
