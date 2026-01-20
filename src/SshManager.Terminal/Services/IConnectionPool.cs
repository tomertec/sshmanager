using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Key for identifying unique connections in the pool.
/// Connections with the same key can be reused.
/// </summary>
public sealed record ConnectionPoolKey(
    string Hostname,
    int Port,
    string Username,
    AuthType AuthType,
    string? PrivateKeyPath)
{
    public static ConnectionPoolKey FromConnectionInfo(TerminalConnectionInfo info) =>
        new(info.Hostname, info.Port, info.Username, info.AuthType, info.PrivateKeyPath);
}

/// <summary>
/// Statistics about the connection pool.
/// </summary>
public sealed record PoolStatistics(
    int TotalConnections,
    int ActiveConnections,
    int IdleConnections,
    int TotalHosts);

/// <summary>
/// Represents a pooled SSH connection that returns to the pool when released.
/// </summary>
public interface IPooledConnection : IAsyncDisposable
{
    /// <summary>
    /// The underlying SSH client.
    /// </summary>
    SshClient Client { get; }

    /// <summary>
    /// Whether the connection is still valid and connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The pool key identifying this connection.
    /// </summary>
    ConnectionPoolKey Key { get; }

    /// <summary>
    /// Creates a new shell stream for this connection.
    /// </summary>
    ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize);
}

/// <summary>
/// Manages a pool of SSH connections for reuse across terminal sessions.
/// </summary>
public interface IConnectionPool
{
    /// <summary>
    /// Acquires a connection from the pool or creates a new one using the factory.
    /// </summary>
    /// <param name="key">The connection pool key.</param>
    /// <param name="factory">Factory to create a new connection if none available.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A pooled connection.</returns>
    Task<IPooledConnection> AcquireAsync(
        ConnectionPoolKey key,
        Func<Task<SshClient>> factory,
        CancellationToken ct = default);

    /// <summary>
    /// Releases a connection back to the pool for reuse.
    /// </summary>
    /// <param name="connection">The pooled connection to release.</param>
    void Release(IPooledConnection connection);

    /// <summary>
    /// Gets current pool statistics.
    /// </summary>
    PoolStatistics GetStatistics();

    /// <summary>
    /// Drains all connections from the pool, closing them gracefully.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task DrainAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether connection pooling is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Updates pool configuration from settings.
    /// </summary>
    void UpdateConfiguration(bool enabled, int maxPerHost, TimeSpan idleTimeout);
}
