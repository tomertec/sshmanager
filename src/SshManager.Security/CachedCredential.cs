using System.Security;

namespace SshManager.Security;

/// <summary>
/// Represents a cached credential stored securely in memory.
/// </summary>
public sealed class CachedCredential : IDisposable
{
    private SecureString? _secureValue;
    private bool _disposed;

    /// <summary>
    /// Gets the type of credential (Password or KeyPassphrase).
    /// </summary>
    public CredentialType Type { get; }

    /// <summary>
    /// Gets the expiration time for this cached credential.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets whether this credential has expired.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    /// <summary>
    /// Creates a new cached credential.
    /// </summary>
    /// <param name="type">The type of credential.</param>
    /// <param name="value">The credential value to cache.</param>
    /// <param name="expiresAt">When the credential should expire.</param>
    public CachedCredential(CredentialType type, string value, DateTimeOffset expiresAt)
    {
        Type = type;
        ExpiresAt = expiresAt;
        _secureValue = value.ToSecureString();
    }

    /// <summary>
    /// Creates a new cached credential with a SecureString value.
    /// </summary>
    /// <param name="type">The type of credential.</param>
    /// <param name="secureValue">The secure credential value to cache.</param>
    /// <param name="expiresAt">When the credential should expire.</param>
    public CachedCredential(CredentialType type, SecureString secureValue, DateTimeOffset expiresAt)
    {
        Type = type;
        ExpiresAt = expiresAt;
        _secureValue = secureValue.Copy();
    }

    /// <summary>
    /// Gets the credential value as a plain string.
    /// WARNING: This creates an unprotected copy of the sensitive data in memory.
    /// </summary>
    /// <returns>The credential value, or null if disposed or empty.</returns>
    public string? GetValue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _secureValue?.ToUnsecureString();
    }

    /// <summary>
    /// Gets a copy of the SecureString value.
    /// </summary>
    /// <returns>A copy of the secure value, or null if disposed.</returns>
    public SecureString? GetSecureValue()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _secureValue?.Copy();
    }

    /// <summary>
    /// Disposes the credential, securely clearing the stored value.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _secureValue?.ClearAndDispose();
        _secureValue = null;
        _disposed = true;
    }
}
