using System.Net.Sockets;

namespace SshManager.Terminal.Services;

/// <summary>
/// Event arguments for network status changes.
/// </summary>
public class NetworkStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets whether the network is currently available.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Gets the time when the status change was detected.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    public NetworkStatusChangedEventArgs(bool isAvailable)
    {
        IsAvailable = isAvailable;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Interface for monitoring network connectivity status.
/// Used to pause/resume reconnection attempts based on network availability.
/// </summary>
public interface INetworkMonitor : IDisposable
{
    /// <summary>
    /// Gets whether network connectivity is currently available.
    /// </summary>
    bool IsNetworkAvailable { get; }

    /// <summary>
    /// Event raised when network availability status changes.
    /// </summary>
    event EventHandler<NetworkStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Starts monitoring network connectivity.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops monitoring network connectivity.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Checks if a specific host is reachable via TCP connection.
    /// </summary>
    /// <param name="hostname">The hostname to check.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the host is reachable, false otherwise.</returns>
    Task<bool> CanReachHostAsync(string hostname, int port, CancellationToken ct = default);

    /// <summary>
    /// Checks if a specific host is reachable via TCP connection with timeout.
    /// </summary>
    /// <param name="hostname">The hostname to check.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="timeout">Connection timeout.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the host is reachable, false otherwise.</returns>
    Task<bool> CanReachHostAsync(string hostname, int port, TimeSpan timeout, CancellationToken ct = default);
}
