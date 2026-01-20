using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Monitors network connectivity using Windows network change events and periodic checks.
/// Provides notifications when network availability changes to support auto-reconnect logic.
/// </summary>
public sealed class NetworkMonitor : INetworkMonitor
{
    private readonly ILogger<NetworkMonitor> _logger;
    private readonly object _lock = new();
    private readonly TimeSpan _defaultHostCheckTimeout = TimeSpan.FromSeconds(5);

    private bool _isNetworkAvailable;
    private bool _isMonitoring;
    private bool _disposed;

    /// <summary>
    /// Gets whether network connectivity is currently available.
    /// </summary>
    public bool IsNetworkAvailable
    {
        get
        {
            lock (_lock)
            {
                return _isNetworkAvailable;
            }
        }
    }

    /// <summary>
    /// Event raised when network availability status changes.
    /// </summary>
    public event EventHandler<NetworkStatusChangedEventArgs>? StatusChanged;

    public NetworkMonitor(ILogger<NetworkMonitor>? logger = null)
    {
        _logger = logger ?? NullLogger<NetworkMonitor>.Instance;
        _isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
    }

    /// <summary>
    /// Starts monitoring network connectivity.
    /// </summary>
    public void StartMonitoring()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NetworkMonitor));
        }

        lock (_lock)
        {
            if (_isMonitoring)
            {
                return;
            }

            _isMonitoring = true;
            _isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();

            // Subscribe to system network change events
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            _logger.LogInformation(
                "Network monitoring started. Current status: {Status}",
                _isNetworkAvailable ? "Available" : "Unavailable");
        }
    }

    /// <summary>
    /// Stops monitoring network connectivity.
    /// </summary>
    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (!_isMonitoring)
            {
                return;
            }

            _isMonitoring = false;

            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            _logger.LogInformation("Network monitoring stopped");
        }
    }

    /// <summary>
    /// Checks if a specific host is reachable via TCP connection.
    /// </summary>
    public Task<bool> CanReachHostAsync(string hostname, int port, CancellationToken ct = default)
    {
        return CanReachHostAsync(hostname, port, _defaultHostCheckTimeout, ct);
    }

    /// <summary>
    /// Checks if a specific host is reachable via TCP connection with timeout.
    /// </summary>
    public async Task<bool> CanReachHostAsync(string hostname, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NetworkMonitor));
        }

        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new ArgumentException("Hostname cannot be null or empty", nameof(hostname));
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            await client.ConnectAsync(hostname, port, timeoutCts.Token);

            _logger.LogDebug("Host {Hostname}:{Port} is reachable", hostname, port);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout - host not reachable within timeout
            _logger.LogDebug("Host {Hostname}:{Port} connection timed out after {Timeout}ms",
                hostname, port, timeout.TotalMilliseconds);
            return false;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("Host {Hostname}:{Port} not reachable: {Error}",
                hostname, port, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking host {Hostname}:{Port} reachability", hostname, port);
            return false;
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        HandleNetworkChange(e.IsAvailable);
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        // When network address changes, recheck availability
        var isAvailable = NetworkInterface.GetIsNetworkAvailable();
        HandleNetworkChange(isAvailable);
    }

    private void HandleNetworkChange(bool isAvailable)
    {
        bool shouldNotify;

        lock (_lock)
        {
            if (_isNetworkAvailable == isAvailable)
            {
                return;
            }

            _isNetworkAvailable = isAvailable;
            shouldNotify = _isMonitoring;
        }

        if (shouldNotify)
        {
            _logger.LogInformation("Network status changed: {Status}",
                isAvailable ? "Available" : "Unavailable");

            try
            {
                StatusChanged?.Invoke(this, new NetworkStatusChangedEventArgs(isAvailable));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking StatusChanged event handler");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMonitoring();

        _logger.LogDebug("NetworkMonitor disposed");
    }
}
