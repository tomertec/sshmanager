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
    /// <remarks>
    /// <para><strong>SECURITY LIMITATION:</strong> This constructor accepts a plain <see cref="string"/> parameter
    /// for the credential value. This means the sensitive data already exists in memory as an unprotected, immutable
    /// string before being converted to a <see cref="SecureString"/> internally via <see cref="SecureStringExtensions.ToSecureString"/>.</para>
    ///
    /// <para><strong>Memory Security Risk:</strong> The plain string parameter remains in memory until garbage collected
    /// and cannot be securely zeroed because strings are immutable in .NET. This creates a window where the credential
    /// could be exposed in memory dumps, debugging sessions, or if memory is swapped to disk.</para>
    ///
    /// <para><strong>Recommendation:</strong> When possible, prefer using the overload that accepts a <see cref="SecureString"/>
    /// directly: <see cref="CachedCredential(CredentialType, SecureString, DateTimeOffset)"/>. This avoids creating a
    /// plain string in the first place.</para>
    ///
    /// <para><strong>Best Practice:</strong> If you must use this constructor, set the source string reference to <c>null</c>
    /// immediately after construction to make it eligible for garbage collection:</para>
    ///
    /// <example>
    /// <code>
    /// string password = GetPasswordFromSomewhere();
    /// var credential = new CachedCredential(CredentialType.Password, password, expiresAt);
    /// password = null; // Allow GC to reclaim the plain string
    /// </code>
    /// </example>
    /// </remarks>
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
