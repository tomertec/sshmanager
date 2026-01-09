using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SshManager.App.Services;

/// <summary>
/// Service that monitors host online/offline status using ICMP ping with TCP fallback.
/// </summary>
public class HostStatusService : IHostStatusService
{
    private readonly ILogger<HostStatusService> _logger;
    private readonly ConcurrentDictionary<Guid, HostRegistration> _registeredHosts = new();
    private readonly ConcurrentDictionary<Guid, HostStatus> _statuses = new();
    private readonly SemaphoreSlim _pingSemaphore = new(10); // Limit concurrent pings
    private const int PingTimeoutMs = 1000;
    private const int TcpTimeoutMs = 1500;

    private sealed record HostRegistration(string Hostname, int Port, TimeSpan CheckInterval);

    public event EventHandler<HostStatusChangedEventArgs>? StatusChanged;

    public HostStatusService(ILogger<HostStatusService> logger)
    {
        _logger = logger;
    }

    public HostStatus? GetStatus(Guid hostId)
    {
        return _statuses.TryGetValue(hostId, out var status) ? status : null;
    }

    public IReadOnlyDictionary<Guid, HostStatus> GetAllStatuses()
    {
        return _statuses;
    }

    public async Task<HostStatus> CheckHostAsync(Guid hostId, string hostname, int port, CancellationToken ct = default)
    {
        await _pingSemaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var pingLatency = await TryPingAsync(hostname);
            if (pingLatency.HasValue)
            {
                return UpdateStatus(hostId, hostname, isOnline: true, pingLatency);
            }

            ct.ThrowIfCancellationRequested();
            var tcpLatency = await TryTcpAsync(hostname, port, ct);
            if (tcpLatency.HasValue)
            {
                _logger.LogDebug("TCP check succeeded for {Hostname}:{Port}", hostname, port);
                return UpdateStatus(hostId, hostname, isOnline: true, tcpLatency);
            }

            return UpdateStatus(hostId, hostname, isOnline: false, latency: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ping host {Hostname}", hostname);
            return UpdateStatus(hostId, hostname, isOnline: false, latency: null);
        }
        finally
        {
            _pingSemaphore.Release();
        }
    }

    public async Task CheckAllHostsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = _registeredHosts
            .Where(kvp => ShouldCheckHost(kvp.Key, kvp.Value, now))
            .Select(kvp => CheckHostAsync(kvp.Key, kvp.Value.Hostname, kvp.Value.Port, ct));
        await Task.WhenAll(tasks);
    }

    public void RegisterHost(Guid hostId, string hostname, int port, int checkIntervalSeconds)
    {
        var interval = checkIntervalSeconds > 0
            ? TimeSpan.FromSeconds(checkIntervalSeconds)
            : TimeSpan.Zero;
        _registeredHosts[hostId] = new HostRegistration(hostname, port, interval);
        _logger.LogDebug("Registered host {HostId} ({Hostname}:{Port}) for status monitoring", hostId, hostname, port);
    }

    public void UnregisterHost(Guid hostId)
    {
        _registeredHosts.TryRemove(hostId, out _);
        _statuses.TryRemove(hostId, out _);
        _logger.LogDebug("Unregistered host {HostId} from status monitoring", hostId);
    }

    public void ClearHosts()
    {
        _registeredHosts.Clear();
        _statuses.Clear();
        _logger.LogDebug("Cleared all registered hosts from status monitoring");
    }

    private async Task<TimeSpan?> TryPingAsync(string hostname)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostname, PingTimeoutMs);
            return reply.Status == IPStatus.Success
                ? TimeSpan.FromMilliseconds(reply.RoundtripTime)
                : null;
        }
        catch (PingException ex)
        {
            _logger.LogDebug("Ping failed for {Hostname}: {Message}", hostname, ex.Message);
            return null;
        }
    }

    private async Task<TimeSpan?> TryTcpAsync(string hostname, int port, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TcpTimeoutMs);
            await client.ConnectAsync(hostname, port, timeoutCts.Token);
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TCP check failed for {Hostname}:{Port}", hostname, port);
            return null;
        }
    }

    private HostStatus UpdateStatus(Guid hostId, string hostname, bool isOnline, TimeSpan? latency)
    {
        var status = new HostStatus(isOnline, latency, DateTimeOffset.UtcNow);
        var oldStatus = _statuses.TryGetValue(hostId, out var existing) ? existing : null;
        _statuses[hostId] = status;

        if (oldStatus?.IsOnline != status.IsOnline)
        {
            _logger.LogDebug("Host {HostId} ({Hostname}) status changed to {Status}",
                hostId, hostname, isOnline ? "online" : "offline");
            StatusChanged?.Invoke(this, new HostStatusChangedEventArgs(hostId, status));
        }

        return status;
    }

    private bool ShouldCheckHost(Guid hostId, HostRegistration registration, DateTimeOffset now)
    {
        if (registration.CheckInterval <= TimeSpan.Zero)
        {
            return false;
        }

        if (!_statuses.TryGetValue(hostId, out var status) || status.LastChecked == null)
        {
            return true;
        }

        return now - status.LastChecked.Value >= registration.CheckInterval;
    }
}
