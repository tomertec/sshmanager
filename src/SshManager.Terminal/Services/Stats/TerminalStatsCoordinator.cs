using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services.Stats;

/// <summary>
/// Coordinates terminal session statistics collection and status bar updates.
/// </summary>
/// <remarks>
/// <para>
/// This service manages the <see cref="ITerminalStatsCollector"/> lifecycle and configures
/// the <see cref="TerminalStatusBar"/> based on connection type. It handles both SSH and
/// serial sessions with appropriate display configurations.
/// </para>
/// <para>
/// <b>Thread Safety:</b> All methods should be called on the UI thread as they interact
/// with WPF controls (TerminalStatusBar).
/// </para>
/// </remarks>
public sealed class TerminalStatsCoordinator : ITerminalStatsCoordinator
{
    private readonly IServerStatsService? _serverStatsService;
    private readonly ILogger<TerminalStatsCoordinator> _logger;

    private ITerminalStatsCollector? _statsCollector;
    private TerminalSession? _session;
    private SshTerminalBridge? _sshBridge;
    private TerminalStatusBar? _statusBar;
    private SerialConnectionInfo? _serialConnectionInfo;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsCollecting => _statsCollector?.IsRunning == true;

    /// <inheritdoc />
    public TerminalStats? CurrentStats => _session?.Stats;

    /// <inheritdoc />
    public event EventHandler<TerminalStats>? StatsUpdated;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalStatsCoordinator"/> class.
    /// </summary>
    /// <param name="serverStatsService">Optional service for collecting server-side statistics via SSH.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public TerminalStatsCoordinator(
        IServerStatsService? serverStatsService = null,
        ILogger<TerminalStatsCoordinator>? logger = null)
    {
        _serverStatsService = serverStatsService;
        _logger = logger ?? NullLogger<TerminalStatsCoordinator>.Instance;
    }

    /// <inheritdoc />
    public void StartForSshSession(TerminalSession session, SshTerminalBridge? bridge, TerminalStatusBar statusBar)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(statusBar);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalStatsCoordinator));
        }

        // For SSH connections, we require a bridge for throughput stats
        // (unless we're attaching to an existing session without a bridge)
        _session = session;
        _sshBridge = bridge;
        _statusBar = statusBar;
        _serialConnectionInfo = null;

        // Create stats collector if needed
        EnsureStatsCollector();

        // Start stats collection with the SSH bridge
        _statsCollector!.Start(session, bridge);

        // Configure status bar for SSH session
        ConfigureStatusBarForSsh();

        _logger.LogDebug("Started stats collection for SSH session {SessionId}", session.Id);
    }

    /// <inheritdoc />
    public void StartForSerialSession(TerminalSession session, SerialConnectionInfo? connectionInfo, TerminalStatusBar statusBar)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(statusBar);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalStatsCoordinator));
        }

        _session = session;
        _sshBridge = null; // Serial sessions don't use SSH bridge
        _statusBar = statusBar;
        _serialConnectionInfo = connectionInfo;

        // Create stats collector if needed
        EnsureStatsCollector();

        // Start stats collection without SSH bridge (serial uses session's serial bridge internally)
        _statsCollector!.Start(session, null);

        // Configure status bar for serial session
        ConfigureStatusBarForSerial();

        _logger.LogDebug("Started stats collection for serial session {SessionId} on port {Port}",
            session.Id, connectionInfo?.PortName ?? "unknown");
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_statsCollector != null)
        {
            _statsCollector.StatsUpdated -= OnStatsUpdated;
            _statsCollector.Stop();
            _logger.LogDebug("Stats collection stopped");
        }

        // Clear references but don't dispose the collector (allows resume)
        _session = null;
        _sshBridge = null;
        _serialConnectionInfo = null;
    }

    /// <inheritdoc />
    public void Pause()
    {
        // Just stop the timer without clearing state
        _statsCollector?.Stop();
        _logger.LogDebug("Stats collection paused");
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (_session == null || _statusBar == null || _statsCollector == null)
        {
            _logger.LogDebug("Cannot resume: missing session, status bar, or stats collector");
            return;
        }

        // Only resume if not already running
        if (!_statsCollector.IsRunning)
        {
            _statsCollector.Start(_session, _sshBridge);
            _logger.LogDebug("Stats collection resumed for session {SessionId}", _session.Id);
        }
    }

    /// <summary>
    /// Ensures the stats collector is created and wired up.
    /// </summary>
    private void EnsureStatsCollector()
    {
        if (_statsCollector == null)
        {
            _statsCollector = new TerminalStatsCollector(_serverStatsService);
            _statsCollector.StatsUpdated += OnStatsUpdated;
        }
    }

    /// <summary>
    /// Configures the status bar for SSH session display.
    /// </summary>
    private void ConfigureStatusBarForSsh()
    {
        if (_statusBar == null || _session == null) return;

        _statusBar.Stats = _session.Stats;
        _statusBar.IsSerialSession = false;
        _statusBar.SerialConnectionInfo = null;
        _statusBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Configures the status bar for serial session display.
    /// </summary>
    private void ConfigureStatusBarForSerial()
    {
        if (_statusBar == null || _session == null) return;

        _statusBar.Stats = _session.Stats;
        _statusBar.IsSerialSession = true;
        _statusBar.SerialConnectionInfo = _serialConnectionInfo;
        _statusBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Handles stats updates from the collector and forwards them to the status bar.
    /// </summary>
    private void OnStatsUpdated(object? sender, TerminalStats stats)
    {
        // Update the status bar display
        if (_statusBar != null)
        {
            _statusBar.Stats = stats;
            _statusBar.UpdateDisplay();
        }

        // Forward the event to any external listeners
        StatsUpdated?.Invoke(this, stats);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_statsCollector != null)
        {
            _statsCollector.StatsUpdated -= OnStatsUpdated;
            _statsCollector.Dispose();
            _statsCollector = null;
        }

        _session = null;
        _sshBridge = null;
        _statusBar = null;
        _serialConnectionInfo = null;

        _logger.LogDebug("TerminalStatsCoordinator disposed");
    }
}
