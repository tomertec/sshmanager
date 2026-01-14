using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for collecting terminal session statistics.
/// Updates session stats on a 1-second interval and server stats every 10 seconds.
/// </summary>
public sealed class TerminalStatsCollector : ITerminalStatsCollector
{
    private readonly IServerStatsService? _serverStatsService;
    private readonly ILogger<TerminalStatsCollector> _logger;
    private readonly DispatcherTimer _timer;

    private TerminalSession? _session;
    private SshTerminalBridge? _bridge;
    private DateTimeOffset _lastStatsTime = DateTimeOffset.UtcNow;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsRunning => _timer.IsEnabled;

    /// <inheritdoc />
    public event EventHandler<TerminalStats>? StatsUpdated;

    public TerminalStatsCollector(
        IServerStatsService? serverStatsService = null,
        ILogger<TerminalStatsCollector>? logger = null)
    {
        _serverStatsService = serverStatsService;
        _logger = logger ?? NullLogger<TerminalStatsCollector>.Instance;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;
    }

    /// <inheritdoc />
    public void Start(TerminalSession session, SshTerminalBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bridge);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalStatsCollector));
        }

        _session = session;
        _bridge = bridge;
        _lastStatsTime = DateTimeOffset.UtcNow;

        if (!_timer.IsEnabled)
        {
            _timer.Start();
            _logger.LogDebug("Stats collection started for session {SessionId}", session.Id);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _logger.LogDebug("Stats collection stopped");
        }
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_session == null || _bridge == null) return;

            var now = DateTimeOffset.UtcNow;

            // Update uptime
            _session.Stats.Uptime = now - _session.CreatedAt;

            // Update throughput from bridge
            _session.Stats.BytesSent = _bridge.TotalBytesSent;
            _session.Stats.BytesReceived = _bridge.TotalBytesReceived;

            // Calculate throughput per second
            var elapsed = (now - _lastStatsTime).TotalSeconds;
            if (elapsed > 0)
            {
                _session.Stats.BytesSentPerSecond = (_bridge.TotalBytesSent - _session.TotalBytesSent) / elapsed;
                _session.Stats.BytesReceivedPerSecond = (_bridge.TotalBytesReceived - _session.TotalBytesReceived) / elapsed;
            }

            // Update session counters for next delta calculation
            _session.TotalBytesSent = _bridge.TotalBytesSent;
            _session.TotalBytesReceived = _bridge.TotalBytesReceived;
            _lastStatsTime = now;

            // Collect server stats via SSH (only every ~10 seconds)
            if (_session.Connection?.IsConnected == true && _serverStatsService != null && now.Second % 10 == 0)
            {
                try
                {
                    var stats = await _serverStatsService.GetStatsAsync(_session.Connection);
                    _session.Stats.CpuUsage = stats.CpuUsage;
                    _session.Stats.MemoryUsage = stats.MemoryUsage;
                    _session.Stats.DiskUsage = stats.DiskUsage;
                    _session.Stats.ServerUptime = stats.ServerUptime;
                }
                catch
                {
                    // Ignore stats collection failures - don't spam logs
                }
            }

            // Notify listeners
            StatsUpdated?.Invoke(this, _session.Stats);
        }
        catch (Exception ex)
        {
            // Catch all exceptions in async void event handler to prevent application crashes
            _logger.LogError(ex, "Error updating terminal stats");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= Timer_Tick;

        _session = null;
        _bridge = null;

        _logger.LogDebug("TerminalStatsCollector disposed");
    }
}
