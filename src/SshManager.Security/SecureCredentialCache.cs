using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Security;

/// <summary>
/// Thread-safe in-memory credential cache with automatic expiration.
/// Credentials are stored using SecureString for enhanced security.
/// </summary>
public sealed class SecureCredentialCache : ICredentialCache
{
    private readonly ConcurrentDictionary<Guid, CachedCredential> _cache = new();
    private readonly ILogger<SecureCredentialCache> _logger;
    private readonly object _timerLock = new();

    private Timer? _cleanupTimer;
    private TimeSpan _timeout = TimeSpan.FromMinutes(15);
    private bool _disposed;
    private bool _cachingEnabled;

    /// <summary>
    /// Interval in seconds for the cleanup timer to check for expired credentials.
    /// 60 seconds provides a good balance between:
    /// - Timely cleanup of expired credentials (security)
    /// - Minimal overhead from frequent timer callbacks (performance)
    /// This is an internal implementation detail - users configure the actual timeout via SetTimeout().
    /// </summary>
    private const int CleanupIntervalSeconds = 60;

    public event EventHandler? CacheCleared;

    /// <summary>
    /// Gets the number of currently cached credentials.
    /// </summary>
    public int Count => _cache.Count;

    public SecureCredentialCache(ILogger<SecureCredentialCache>? logger = null)
    {
        _logger = logger ?? NullLogger<SecureCredentialCache>.Instance;
        // Timer is not started here - it will be started when caching is enabled via EnableCaching()
        _logger.LogDebug("SecureCredentialCache initialized with {Timeout} minute timeout (caching disabled by default)", _timeout.TotalMinutes);
    }

    /// <inheritdoc />
    public void CacheCredential(Guid hostId, CachedCredential credential)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(credential);

        // Remove any existing credential for this host
        if (_cache.TryRemove(hostId, out var existing))
        {
            existing.Dispose();
            _logger.LogDebug("Replaced existing cached credential for host {HostId}", hostId);
        }

        _cache[hostId] = credential;
        _logger.LogDebug("Cached {CredentialType} credential for host {HostId}, expires at {ExpiresAt}",
            credential.Type, hostId, credential.ExpiresAt);
    }

    /// <inheritdoc />
    public CachedCredential? GetCachedCredential(Guid hostId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cache.TryGetValue(hostId, out var credential))
        {
            _logger.LogDebug("No cached credential found for host {HostId}", hostId);
            return null;
        }

        if (credential.IsExpired)
        {
            _logger.LogDebug("Cached credential for host {HostId} has expired", hostId);
            // Use TryRemove with comparer to ensure we only remove if it's still the same expired credential
            // This prevents removing a newly cached credential that replaced the expired one
            if (_cache.TryRemove(KeyValuePair.Create(hostId, credential)))
            {
                credential.Dispose();
            }
            return null;
        }

        _logger.LogDebug("Retrieved cached {CredentialType} credential for host {HostId}",
            credential.Type, hostId);
        return credential;
    }

    /// <inheritdoc />
    public void RemoveCredential(Guid hostId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cache.TryRemove(hostId, out var credential))
        {
            credential.Dispose();
            _logger.LogDebug("Removed cached credential for host {HostId}", hostId);
        }
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var count = _cache.Count;
        foreach (var kvp in _cache)
        {
            if (_cache.TryRemove(kvp.Key, out var credential))
            {
                credential.Dispose();
            }
        }

        _logger.LogInformation("Cleared {Count} cached credentials", count);
        CacheCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetTimeout(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be a positive value");
        }

        _timeout = timeout;
        _logger.LogInformation("Credential cache timeout set to {Timeout} minutes", timeout.TotalMinutes);
    }

    /// <summary>
    /// Enables or disables credential caching. When enabled, starts the cleanup timer.
    /// When disabled, stops the cleanup timer and clears any cached credentials.
    /// </summary>
    /// <param name="enabled">True to enable caching, false to disable.</param>
    public void EnableCaching(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_timerLock)
        {
            if (_cachingEnabled == enabled)
                return;

            _cachingEnabled = enabled;

            if (enabled)
            {
                StartCleanupTimer();
                _logger.LogInformation("Credential caching enabled");
            }
            else
            {
                StopCleanupTimer();
                ClearAll();
                _logger.LogInformation("Credential caching disabled and cache cleared");
            }
        }
    }

    /// <summary>
    /// Gets whether credential caching is currently enabled.
    /// </summary>
    public bool IsCachingEnabled
    {
        get
        {
            lock (_timerLock)
            {
                return _cachingEnabled;
            }
        }
    }

    /// <inheritdoc />
    public bool IsCredentialCached(Guid hostId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cache.TryGetValue(hostId, out var credential))
            return false;

        if (credential.IsExpired)
        {
            // Use TryRemove with KeyValuePair to ensure we only remove if it's still the same expired credential.
            // This prevents a TOCTOU race condition where another thread could replace the credential
            // between our IsExpired check and the removal, causing us to remove the wrong credential.
            if (_cache.TryRemove(KeyValuePair.Create(hostId, credential)))
            {
                credential.Dispose();
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a new CachedCredential with the current timeout setting.
    /// </summary>
    /// <param name="type">The type of credential.</param>
    /// <param name="value">The credential value.</param>
    /// <returns>A new CachedCredential instance.</returns>
    public CachedCredential CreateCredential(CredentialType type, string value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CachedCredential(type, value, DateTimeOffset.UtcNow.Add(_timeout));
    }

    private void StartCleanupTimer()
    {
        lock (_timerLock)
        {
            // Don't start if already running
            if (_cleanupTimer != null)
                return;

            _cleanupTimer = new Timer(
                CleanupExpiredCredentials,
                null,
                TimeSpan.FromSeconds(CleanupIntervalSeconds),
                TimeSpan.FromSeconds(CleanupIntervalSeconds));
        }
    }

    private void StopCleanupTimer()
    {
        lock (_timerLock)
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
        }
    }

    private void CleanupExpiredCredentials(object? state)
    {
        if (_disposed)
            return;

        var expiredCount = 0;
        
        // Take a snapshot of keys to avoid issues with concurrent modification.
        // While ConcurrentDictionary is thread-safe, iterating while removing
        // can be inefficient and may skip items if the dictionary is modified.
        var keysSnapshot = _cache.Keys.ToArray();
        
        foreach (var key in keysSnapshot)
        {
            // Re-check if the key still exists and if the credential is expired.
            // This handles the case where another thread may have already removed
            // or replaced the credential between our snapshot and this check.
            if (_cache.TryGetValue(key, out var credential) && credential.IsExpired)
            {
                // Use TryRemove with KeyValuePair to ensure we only remove if it's 
                // still the same expired credential (prevents TOCTOU race condition)
                if (_cache.TryRemove(KeyValuePair.Create(key, credential)))
                {
                    credential.Dispose();
                    expiredCount++;
                }
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired credentials", expiredCount);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_timerLock)
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
        }

        // Clear all cached credentials securely
        foreach (var kvp in _cache)
        {
            if (_cache.TryRemove(kvp.Key, out var credential))
            {
                credential.Dispose();
            }
        }

        _logger.LogDebug("SecureCredentialCache disposed");
    }
}
