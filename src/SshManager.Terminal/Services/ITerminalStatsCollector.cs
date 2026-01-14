using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service interface for collecting terminal session statistics.
/// </summary>
public interface ITerminalStatsCollector : IDisposable
{
    /// <summary>
    /// Starts collecting stats for the specified session.
    /// </summary>
    /// <param name="session">The terminal session to collect stats for.</param>
    /// <param name="bridge">The SSH terminal bridge providing throughput data.</param>
    void Start(TerminalSession session, SshTerminalBridge bridge);

    /// <summary>
    /// Stops collecting stats.
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets whether stats collection is currently active.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Event raised when stats are updated.
    /// </summary>
    event EventHandler<TerminalStats>? StatsUpdated;
}
