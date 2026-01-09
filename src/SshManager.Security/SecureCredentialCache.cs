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

    private const int CleanupIntervalSeconds = 60; // Check for expired credentials every minute

    public event EventHandler? CacheCleared;

    /// <summary>
    /// Gets the number of currently cached credentials.
    /// </summary>
    public int Count => _cache.Count;

    public SecureCredentialCache(ILogger<SecureCredentialCache>? logger = null)
    {
        _logger = logger ?? NullLogger<SecureCredentialCache>.Instance;
        StartCleanupTimer();
        _logger.LogDebug("SecureCredentialCache initialized with {Timeout} minute timeout", _timeout.TotalMinutes);
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

    /// <inheritdoc />
    public bool IsCredentialCached(Guid hostId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cache.TryGetValue(hostId, out var credential))
            return false;

        if (credential.IsExpired)
        {
            RemoveCredential(hostId);
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
            _cleanupTimer = new Timer(
                CleanupExpiredCredentials,
                null,
                TimeSpan.FromSeconds(CleanupIntervalSeconds),
                TimeSpan.FromSeconds(CleanupIntervalSeconds));
        }
    }

    private void CleanupExpiredCredentials(object? state)
    {
        if (_disposed)
            return;

        var expiredCount = 0;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsExpired)
            {
                if (_cache.TryRemove(kvp.Key, out var credential))
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
