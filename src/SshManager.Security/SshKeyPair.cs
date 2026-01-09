namespace SshManager.Security;

/// <summary>
/// Represents a generated SSH key pair.
/// </summary>
public sealed class SshKeyPair : IDisposable
{
    /// <summary>
    /// The type of key.
    /// </summary>
    public SshKeyType KeyType { get; init; }

    /// <summary>
    /// Private key in OpenSSH format (PEM).
    /// </summary>
    public string PrivateKey { get; init; } = "";

    /// <summary>
    /// Public key in OpenSSH format (ssh-rsa/ssh-ed25519 ... comment).
    /// </summary>
    public string PublicKey { get; init; } = "";

    /// <summary>
    /// SHA256 fingerprint of the public key.
    /// </summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>
    /// Optional comment embedded in the key.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Whether the private key is encrypted with a passphrase.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// When the key was generated.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Note: strings are immutable in .NET, so we can't securely clear them.
        // In a production environment, consider using SecureString or byte arrays
        // that can be zeroed out.
    }
}
