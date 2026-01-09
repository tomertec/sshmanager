namespace SshManager.Security;

/// <summary>
/// Represents encrypted data for cloud synchronization.
/// Contains all metadata needed to decrypt the data on another device.
/// </summary>
public class EncryptedSyncData
{
    /// <summary>
    /// Schema version for future compatibility.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Encryption algorithm used (e.g., "AES-256-GCM").
    /// </summary>
    public string Algorithm { get; set; } = "AES-256-GCM";

    /// <summary>
    /// Key derivation function used (e.g., "Argon2id").
    /// </summary>
    public string KdfAlgorithm { get; set; } = "Argon2id";

    /// <summary>
    /// Argon2id parameters for key derivation.
    /// </summary>
    public Argon2Parameters KdfParameters { get; set; } = new();

    /// <summary>
    /// Base64-encoded salt used for key derivation.
    /// </summary>
    public string Salt { get; set; } = "";

    /// <summary>
    /// Base64-encoded encrypted data (nonce + ciphertext + tag).
    /// </summary>
    public string Data { get; set; } = "";

    /// <summary>
    /// Unique identifier of the device that last modified this data.
    /// </summary>
    public string DeviceId { get; set; } = "";

    /// <summary>
    /// Human-readable name of the device that last modified this data.
    /// </summary>
    public string DeviceName { get; set; } = "";

    /// <summary>
    /// Timestamp when the data was last modified.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; }
}

/// <summary>
/// Argon2id parameters for reproducible key derivation.
/// </summary>
public class Argon2Parameters
{
    /// <summary>
    /// Memory size in KB (default: 65536 = 64 MB as per OWASP recommendation).
    /// </summary>
    public int MemorySize { get; set; } = 65536;

    /// <summary>
    /// Number of iterations (default: 3 as per OWASP recommendation).
    /// </summary>
    public int Iterations { get; set; } = 3;

    /// <summary>
    /// Degree of parallelism (default: 4).
    /// </summary>
    public int Parallelism { get; set; } = 4;
}
