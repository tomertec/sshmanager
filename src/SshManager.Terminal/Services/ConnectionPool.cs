using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Data.Repositories;

namespace SshManager.Terminal.Services;

/// <summary>
/// Thread-safe pool of SSH connections for reuse across terminal sessions.
/// </summary>
/// <remarks>
/// <para>
/// This service manages a pool of SSH connections to improve performance when opening
/// multiple terminal sessions to the same hosts. Instead of creating a new TCP connection
/// and SSH handshake for each session, connections can be reused from the pool.
/// </para>
/// <para>
/// <b>Threading:</b> All public methods are thread-safe. The pool uses ConcurrentDictionary
/// and per-host locks to safely manage connections across multiple UI threads.
/// </para>
/// <para>
/// <b>Resource Management:</b> Idle connections are automatically cleaned up by a background
/// timer. The cleanup interval is fixed at 30 seconds, while the idle timeout is configurable
/// via settings (default: 5 minutes).
/// </para>
/// <para>
/// <b>Configuration:</b> Pool behavior is controlled by three settings:
/// - EnableConnectionPooling: Master on/off switch
/// - ConnectionPoolMaxPerHost: Max connections per unique host (default: 3)
/// - ConnectionPoolIdleTimeoutSeconds: Idle timeout before cleanup (default: 300)
/// </para>
/// </remarks>
public sealed class ConnectionPool : IConnectionPool, IDisposable, IAsyncDisposable
{
    private readonly ILogger<ConnectionPool> _logger;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ConcurrentDictionary<ConnectionPoolKey, PoolEntry> _pools = new();
    private readonly Timer _cleanupTimer;
    private readonly object _lock = new();

    private bool _isEnabled;
    private int _maxPerHost = 3;
    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(300);
    private bool _disposed;

    public ConnectionPool(
        ISettingsRepository settingsRepo,
        ILogger<ConnectionPool>? logger = null)
    {
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _logger = logger ?? NullLogger<ConnectionPool>.Instance;

        // Run cleanup every 30 seconds
        _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Load initial settings asynchronously without blocking constructor
        // Fire-and-forget with proper exception handling
        _ = LoadSettingsWithExceptionHandlingAsync();
    }

    /// <summary>
    /// Wrapper for LoadSettingsAsync that ensures exceptions are logged and don't go unobserved.
    /// This method is fire-and-forget safe.
    /// </summary>
    private async Task LoadSettingsWithExceptionHandlingAsync()
    {
        try
        {
            await LoadSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // This catch ensures the exception doesn't go unobserved
            // LoadSettingsAsync already logs, but this provides additional safety
            _logger.LogError(ex, "Unhandled exception in background settings load");
        }
    }

    public bool IsEnabled => _isEnabled;

    public async Task<IPooledConnection> AcquireAsync(
        ConnectionPoolKey key,
        Func<Task<SshClient>> factory,
        CancellationToken ct = default)
    {
        if (!_isEnabled)
        {
            // Pooling disabled - create new connection directly
            _logger.LogDebug("Connection pooling disabled, creating direct connection to {Host}:{Port}",
                key.Hostname, key.Port);
            var client = await factory();
            return new PooledConnection(key, client, this, isPooled: false);
        }

        var entry = _pools.GetOrAdd(key, _ => new PoolEntry(_maxPerHost));

        // Try to get an idle connection
        if (entry.TryAcquire(out var existingClient))
        {
            if (existingClient.IsConnected)
            {
                _logger.LogDebug("Reusing pooled connection to {Host}:{Port} (user: {Username}, auth: {AuthType})",
                    key.Hostname, key.Port, key.Username, key.AuthType);
                return new PooledConnection(key, existingClient, this, isPooled: true);
            }
            else
            {
                // Connection is stale, dispose it
                _logger.LogDebug("Disposing stale pooled connection to {Host}:{Port}",
                    key.Hostname, key.Port);
                try
                {
                    existingClient.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error disposing stale connection");
                }
            }
        }

        // Create new connection
        _logger.LogDebug("Creating new connection to {Host}:{Port} (pool miss, user: {Username})",
            key.Hostname, key.Port, key.Username);
        var newClient = await factory();
        return new PooledConnection(key, newClient, this, isPooled: true);
    }

    public void Release(IPooledConnection connection)
    {
        if (connection is not PooledConnection pooled || !pooled.IsPooled)
        {
            // Not a pooled connection - just dispose
            _logger.LogDebug("Disposing non-pooled connection");
            try
            {
                connection.Client.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing non-pooled connection");
            }
            return;
        }

        if (!_isEnabled || !connection.IsConnected)
        {
            _logger.LogDebug("Disposing disconnected or disabled pooled connection to {Host}:{Port}",
                connection.Key.Hostname, connection.Key.Port);
            try
            {
                connection.Client.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing connection");
            }
            return;
        }

        if (_pools.TryGetValue(connection.Key, out var entry))
        {
            if (entry.TryRelease(connection.Client))
            {
                _logger.LogDebug("Released connection back to pool for {Host}:{Port} (user: {Username})",
                    connection.Key.Hostname, connection.Key.Port, connection.Key.Username);
                return;
            }
        }

        // Pool full or not found - dispose
        _logger.LogDebug("Pool full or not found for {Host}:{Port}, disposing connection",
            connection.Key.Hostname, connection.Key.Port);
        try
        {
            connection.Client.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing connection");
        }
    }

    public PoolStatistics GetStatistics()
    {
        var total = 0;
        var active = 0;
        var idle = 0;
        var hosts = 0;

        foreach (var entry in _pools.Values)
        {
            hosts++;
            var stats = entry.GetStats();
            total += stats.total;
            active += stats.active;
            idle += stats.idle;
        }

        return new PoolStatistics(total, active, idle, hosts);
    }

    public async Task DrainAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Draining connection pool...");

        var drainedCount = 0;
        foreach (var kvp in _pools)
        {
            drainedCount += kvp.Value.Drain();
        }

        _pools.Clear();

        _logger.LogInformation("Connection pool drained: {Count} connections closed", drainedCount);
        await Task.CompletedTask;
    }

    public void UpdateConfiguration(bool enabled, int maxPerHost, TimeSpan idleTimeout)
    {
        lock (_lock)
        {
            var wasEnabled = _isEnabled;
            _isEnabled = enabled;
            _maxPerHost = maxPerHost;
            _idleTimeout = idleTimeout;

            if (wasEnabled && !enabled)
            {
                _logger.LogInformation("Connection pooling disabled, pool will drain on next cleanup cycle");
            }
        }

        _logger.LogInformation("Connection pool configuration updated: Enabled={Enabled}, MaxPerHost={Max}, IdleTimeout={Timeout}s",
            enabled, maxPerHost, idleTimeout.TotalSeconds);
    }

    /// <summary>
    /// Loads connection pool settings from the database.
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsRepo.GetAsync();
            UpdateConfiguration(
                settings.EnableConnectionPooling,
                settings.ConnectionPoolMaxPerHost,
                TimeSpan.FromSeconds(settings.ConnectionPoolIdleTimeoutSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load connection pool settings, using defaults");
        }
    }

    /// <summary>
    /// Background timer callback that cleans up idle connections.
    /// </summary>
    private void CleanupIdleConnections(object? state)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        var cleanedCount = 0;

        foreach (var kvp in _pools)
        {
            cleanedCount += kvp.Value.CleanupIdle(_idleTimeout, now, _logger);
        }

        if (cleanedCount > 0)
        {
            _logger.LogDebug("Cleanup cycle removed {Count} idle connection(s)", cleanedCount);
        }
    }

    /// <summary>
    /// Synchronously disposes the connection pool. For better cleanup, use DisposeAsync.
    /// </summary>
    public void Dispose()
    {
        // For synchronous Dispose, we can't await async operations
        // We use synchronous disposal but this may block if SSH connections are slow to disconnect
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing ConnectionPool (synchronous)");

        _cleanupTimer.Dispose();

        var totalClosed = 0;
        foreach (var entry in _pools.Values)
        {
            totalClosed += entry.Drain();
        }

        _pools.Clear();

        _logger.LogInformation("ConnectionPool disposed: {Count} connections closed", totalClosed);
    }

    /// <summary>
    /// Asynchronously disposes the connection pool, allowing SSH connections to disconnect gracefully.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing ConnectionPool (asynchronous)");

        // Dispose the timer synchronously (doesn't have async dispose)
        await _cleanupTimer.DisposeAsync().ConfigureAwait(false);

        var totalClosed = 0;

        // Process each pool entry asynchronously
        var drainTasks = new List<Task<int>>();
        foreach (var entry in _pools.Values)
        {
            drainTasks.Add(Task.Run(() => entry.DrainAsync()));
        }

        // Wait for all drain operations to complete
        var results = await Task.WhenAll(drainTasks).ConfigureAwait(false);
        totalClosed = results.Sum();

        _pools.Clear();

        _logger.LogInformation("ConnectionPool disposed asynchronously: {Count} connections closed", totalClosed);
    }

    /// <summary>
    /// Entry for a single host in the pool.
    /// </summary>
    /// <remarks>
    /// Each PoolEntry manages connections for a unique combination of hostname, port, username,
    /// and authentication type. Thread-safe for concurrent access.
    /// </remarks>
    private sealed class PoolEntry
    {
        private readonly object _entryLock = new();
        private readonly List<PooledClient> _clients = new();
        private readonly int _maxSize;

        public PoolEntry(int maxSize)
        {
            _maxSize = maxSize;
        }

        /// <summary>
        /// Tries to acquire an idle connection from the pool.
        /// </summary>
        public bool TryAcquire(out SshClient client)
        {
            lock (_entryLock)
            {
                var idle = _clients.FirstOrDefault(c => !c.InUse);
                if (idle != null)
                {
                    idle.InUse = true;
                    idle.LastUsed = DateTime.UtcNow;
                    client = idle.Client;
                    return true;
                }
            }

            client = null!;
            return false;
        }

        /// <summary>
        /// Tries to release a connection back to the pool.
        /// </summary>
        public bool TryRelease(SshClient client)
        {
            lock (_entryLock)
            {
                // Check if this client is already in the pool
                var entry = _clients.FirstOrDefault(c => ReferenceEquals(c.Client, client));
                if (entry != null)
                {
                    entry.InUse = false;
                    entry.LastUsed = DateTime.UtcNow;
                    return true;
                }

                // Not in pool - add if room
                if (_clients.Count < _maxSize)
                {
                    _clients.Add(new PooledClient
                    {
                        Client = client,
                        InUse = false,
                        LastUsed = DateTime.UtcNow
                    });
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets statistics about this pool entry.
        /// </summary>
        public (int total, int active, int idle) GetStats()
        {
            lock (_entryLock)
            {
                var active = _clients.Count(c => c.InUse);
                return (_clients.Count, active, _clients.Count - active);
            }
        }

        /// <summary>
        /// Cleans up idle connections that have exceeded the timeout.
        /// </summary>
        /// <returns>Number of connections cleaned up.</returns>
        public int CleanupIdle(TimeSpan idleTimeout, DateTime now, ILogger logger)
        {
            lock (_entryLock)
            {
                var toRemove = _clients
                    .Where(c => !c.InUse && (now - c.LastUsed) > idleTimeout)
                    .ToList();

                foreach (var item in toRemove)
                {
                    _clients.Remove(item);
                    try
                    {
                        item.Client.Dispose();
                        logger.LogDebug("Closed idle pooled connection (idle for {IdleTime:F0}s)",
                            (now - item.LastUsed).TotalSeconds);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Error disposing idle connection");
                    }
                }

                return toRemove.Count;
            }
        }

        /// <summary>
        /// Drains all connections from this entry synchronously.
        /// </summary>
        /// <returns>Number of connections drained.</returns>
        public int Drain()
        {
            lock (_entryLock)
            {
                var count = _clients.Count;
                foreach (var item in _clients)
                {
                    try
                    {
                        item.Client.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors during drain
                    }
                }
                _clients.Clear();
                return count;
            }
        }

        /// <summary>
        /// Drains all connections from this entry asynchronously.
        /// Allows SSH connections to disconnect gracefully without blocking.
        /// </summary>
        /// <returns>Number of connections drained.</returns>
        public int DrainAsync()
        {
            List<PooledClient> clientsToDispose;

            // Lock only to copy the clients list, then dispose outside the lock
            lock (_entryLock)
            {
                clientsToDispose = _clients.ToList();
                _clients.Clear();
            }

            // Dispose connections outside the lock to avoid blocking
            var count = clientsToDispose.Count;
            foreach (var item in clientsToDispose)
            {
                try
                {
                    // SSH.NET's Dispose can block during disconnection
                    // Running in Task.Run allows concurrent disposal
                    item.Client.Dispose();
                }
                catch
                {
                    // Ignore disposal errors during drain
                }
            }

            return count;
        }

        /// <summary>
        /// Represents a pooled client with usage tracking.
        /// </summary>
        private sealed class PooledClient
        {
            public required SshClient Client { get; init; }
            public bool InUse { get; set; }
            public DateTime LastUsed { get; set; }
        }
    }

    /// <summary>
    /// Wrapper for a pooled connection that returns to the pool when released.
    /// </summary>
    private sealed class PooledConnection : IPooledConnection
    {
        private readonly ConnectionPool _pool;
        private bool _disposed;

        public PooledConnection(ConnectionPoolKey key, SshClient client, ConnectionPool pool, bool isPooled)
        {
            Key = key;
            Client = client;
            _pool = pool;
            IsPooled = isPooled;
        }

        public SshClient Client { get; }
        public ConnectionPoolKey Key { get; }
        public bool IsPooled { get; }
        public bool IsConnected => !_disposed && Client.IsConnected;

        public ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PooledConnection));
            }

            return Client.CreateShellStream(terminalName, columns, rows, width, height, bufferSize);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            _pool.Release(this);
            return ValueTask.CompletedTask;
        }
    }
}
