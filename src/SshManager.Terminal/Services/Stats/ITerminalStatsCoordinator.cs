using SshManager.Terminal.Controls;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services.Stats;

/// <summary>
/// Coordinates terminal session statistics collection and status bar updates.
/// </summary>
/// <remarks>
/// <para>
/// This service abstracts the stats collection lifecycle from <see cref="Controls.SshTerminalControl"/>,
/// managing the <see cref="ITerminalStatsCollector"/> instance and configuring the status bar
/// based on connection type (SSH vs Serial).
/// </para>
/// <para>
/// <b>Responsibilities:</b>
/// <list type="bullet">
///   <item>Orchestrate stats collection lifecycle (start/stop/pause/resume)</item>
///   <item>Configure status bar based on connection type</item>
///   <item>Handle stats update events and forward to status bar</item>
///   <item>Manage stats collector instance lifetime</item>
/// </list>
/// </para>
/// </remarks>
public interface ITerminalStatsCoordinator : IDisposable
{
    /// <summary>
    /// Gets whether stats collection is currently active.
    /// </summary>
    bool IsCollecting { get; }

    /// <summary>
    /// Gets the current terminal stats, or null if not collecting.
    /// </summary>
    TerminalStats? CurrentStats { get; }

    /// <summary>
    /// Starts stats collection for an SSH session.
    /// </summary>
    /// <param name="session">The terminal session to collect stats for.</param>
    /// <param name="bridge">The SSH terminal bridge providing throughput data.</param>
    /// <param name="statusBar">The status bar control to update with stats.</param>
    /// <remarks>
    /// Configures the status bar for SSH display mode and begins periodic stats collection.
    /// The status bar will show SSH-specific metrics (latency, CPU, memory, disk, server uptime).
    /// </remarks>
    void StartForSshSession(TerminalSession session, SshTerminalBridge? bridge, TerminalStatusBar statusBar);

    /// <summary>
    /// Starts stats collection for a serial session.
    /// </summary>
    /// <param name="session">The terminal session to collect stats for.</param>
    /// <param name="connectionInfo">The serial connection info for status bar display.</param>
    /// <param name="statusBar">The status bar control to update with stats.</param>
    /// <remarks>
    /// Configures the status bar for serial display mode and begins periodic stats collection.
    /// The status bar will show serial-specific metrics (port, baud rate, settings, handshake, throughput).
    /// </remarks>
    void StartForSerialSession(TerminalSession session, SerialConnectionInfo? connectionInfo, TerminalStatusBar statusBar);

    /// <summary>
    /// Stops stats collection and hides the status bar.
    /// </summary>
    void Stop();

    /// <summary>
    /// Pauses stats collection without disposing resources.
    /// </summary>
    /// <remarks>
    /// Used when the control is unloaded (e.g., tab switched away) to conserve resources.
    /// Call <see cref="Resume"/> when the control is loaded again.
    /// </remarks>
    void Pause();

    /// <summary>
    /// Resumes stats collection after a pause.
    /// </summary>
    /// <remarks>
    /// Only effective if collection was previously started and then paused.
    /// Requires the session and bridge to still be valid.
    /// </remarks>
    void Resume();

    /// <summary>
    /// Event raised when stats are updated.
    /// </summary>
    event EventHandler<TerminalStats>? StatsUpdated;
}
