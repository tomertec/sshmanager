namespace SshManager.Security;

/// <summary>
/// Interface for in-memory credential caching with automatic expiration.
/// </summary>
public interface ICredentialCache : IDisposable
{
    /// <summary>
    /// Caches a credential for the specified host.
    /// </summary>
    /// <param name="hostId">The unique identifier of the host.</param>
    /// <param name="credential">The credential to cache.</param>
    void CacheCredential(Guid hostId, CachedCredential credential);

    /// <summary>
    /// Retrieves a cached credential for the specified host, if it exists and hasn't expired.
    /// </summary>
    /// <param name="hostId">The unique identifier of the host.</param>
    /// <returns>The cached credential, or null if not found or expired.</returns>
    CachedCredential? GetCachedCredential(Guid hostId);

    /// <summary>
    /// Removes a specific credential from the cache.
    /// </summary>
    /// <param name="hostId">The unique identifier of the host.</param>
    void RemoveCredential(Guid hostId);

    /// <summary>
    /// Clears all cached credentials.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Sets the timeout duration for cached credentials.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    void SetTimeout(TimeSpan timeout);

    /// <summary>
    /// Enables or disables credential caching. When enabled, starts the cleanup timer.
    /// When disabled, stops the cleanup timer and clears any cached credentials.
    /// </summary>
    /// <param name="enabled">True to enable caching, false to disable.</param>
    void EnableCaching(bool enabled);

    /// <summary>
    /// Gets whether credential caching is currently enabled.
    /// </summary>
    bool IsCachingEnabled { get; }

    /// <summary>
    /// Checks if a credential is cached for the specified host.
    /// </summary>
    /// <param name="hostId">The unique identifier of the host.</param>
    /// <returns>True if a valid (non-expired) credential exists, false otherwise.</returns>
    bool IsCredentialCached(Guid hostId);

    /// <summary>
    /// Gets the number of currently cached credentials.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Event raised when all credentials are cleared (e.g., on timeout or manual clear).
    /// </summary>
    event EventHandler? CacheCleared;
}
