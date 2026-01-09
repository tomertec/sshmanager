using System.ComponentModel.DataAnnotations;

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
    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Full path to the private key file.
    /// </summary>
    [Required]
    [StringLength(500)]
    public string PrivateKeyPath { get; set; } = "";

    /// <summary>
    /// The type of key (RSA, Ed25519, ECDSA, etc.).
    /// </summary>
    public int KeyType { get; set; }

    /// <summary>
    /// The key size in bits (e.g., 2048, 4096 for RSA, 256/384/521 for ECDSA).
    /// </summary>
    public int KeySize { get; set; }

    /// <summary>
    /// SHA256 fingerprint of the public key.
    /// </summary>
    [Required]
    [StringLength(100)]
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
