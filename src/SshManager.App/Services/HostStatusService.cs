using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SshManager.App.Services;

/// <summary>
/// Service that monitors host online/offline status using ICMP ping with TCP fallback.
/// Uses parallel checking with configurable concurrency for better performance with large host lists.
/// </summary>
public class HostStatusService : IHostStatusService
{
    private readonly ILogger<HostStatusService> _logger;
    private readonly ConcurrentDictionary<Guid, HostRegistration> _registeredHosts = new();
    private readonly ConcurrentDictionary<Guid, HostStatus> _statuses = new();

    // Configurable concurrency limiter - allows tuning based on network conditions
    private readonly SemaphoreSlim _concurrencyLimiter;
    private const int DefaultMaxConcurrency = 10;
    private const int PingTimeoutMs = 1000;
    private const int TcpTimeoutMs = 1500;

    private sealed record HostRegistration(string Hostname, int Port, TimeSpan CheckInterval);

    public event EventHandler<HostStatusChangedEventArgs>? StatusChanged;

    public HostStatusService(ILogger<HostStatusService> logger, int maxConcurrency = DefaultMaxConcurrency)
    {
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(Math.Max(1, maxConcurrency));
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
        await _concurrencyLimiter.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();

            // Run ping and TCP checks in parallel for faster results
            var pingTask = TryPingAsync(hostname, ct);
            var tcpTask = TryTcpAsync(hostname, port, ct);

            await Task.WhenAll(pingTask, tcpTask);

            var pingLatencyMs = pingTask.Result;
            var tcpLatencyMs = tcpTask.Result;

            // Determine online status and build enhanced status
            var isOnline = pingLatencyMs.HasValue || tcpLatencyMs.HasValue;
            var isPortOpen = tcpLatencyMs.HasValue;

            var status = new HostStatus
            {
                HostId = hostId,
                IsOnline = isOnline,
                PingLatencyMs = pingLatencyMs,
                IsPortOpen = isPortOpen,
                TcpLatencyMs = tcpLatencyMs,
                LastChecked = DateTimeOffset.UtcNow
            };

            return UpdateStatus(hostId, hostname, status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check host {Hostname}", hostname);
            return UpdateStatus(hostId, hostname, new HostStatus
            {
                HostId = hostId,
                IsOnline = false,
                IsPortOpen = false,
                LastChecked = DateTimeOffset.UtcNow
            });
        }
        finally
        {
            _concurrencyLimiter.Release();
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

    private async Task<int?> TryPingAsync(string hostname, CancellationToken ct = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostname, PingTimeoutMs);
            return reply.Status == IPStatus.Success
                ? (int)reply.RoundtripTime
                : null;
        }
        catch (PingException ex)
        {
            _logger.LogDebug("Ping failed for {Hostname}: {Message}", hostname, ex.Message);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<int?> TryTcpAsync(string hostname, int port, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TcpTimeoutMs);
            await client.ConnectAsync(hostname, port, timeoutCts.Token);
            stopwatch.Stop();
            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TCP check failed for {Hostname}:{Port}", hostname, port);
            return null;
        }
    }

    private HostStatus UpdateStatus(Guid hostId, string hostname, HostStatus status)
    {
        var oldStatus = _statuses.TryGetValue(hostId, out var existing) ? existing : null;
        _statuses[hostId] = status;

        // Notify on status change (online/offline or level change)
        if (oldStatus?.IsOnline != status.IsOnline || oldStatus?.Level != status.Level)
        {
            _logger.LogDebug("Host {HostId} ({Hostname}) status changed to {Level} (online={IsOnline}, portOpen={PortOpen})",
                hostId, hostname, status.Level, status.IsOnline, status.IsPortOpen);
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
