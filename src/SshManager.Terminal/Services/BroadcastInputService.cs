using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for broadcasting keyboard input to multiple terminal sessions.
/// </summary>
public sealed class BroadcastInputService : IBroadcastInputService
{
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ILogger<BroadcastInputService> _logger;

    public BroadcastInputService(
        ITerminalSessionManager sessionManager,
        ILogger<BroadcastInputService>? logger = null)
    {
        _sessionManager = sessionManager;
        _logger = logger ?? NullLogger<BroadcastInputService>.Instance;
    }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _sessionManager.IsBroadcastMode;
        set => _sessionManager.IsBroadcastMode = value;
    }

    /// <inheritdoc />
    public int TargetSessionCount => _sessionManager.BroadcastSelectedCount;

    /// <inheritdoc />
    public void SendToSelected(byte[] data)
    {
        if (!IsEnabled) return;

        var sessions = _sessionManager.GetBroadcastSessions().ToList();
        if (sessions.Count == 0) return;

        _logger.LogDebug("Broadcasting {ByteCount} bytes to {SessionCount} sessions",
            data.Length, sessions.Count);

        foreach (var session in sessions)
        {
            SendToSession(session, data);
        }
    }

    /// <inheritdoc />
    public void SendToAll(byte[] data)
    {
        var sessions = _sessionManager.Sessions
            .Where(s => s.IsConnected)
            .ToList();

        if (sessions.Count == 0) return;

        _logger.LogDebug("Broadcasting {ByteCount} bytes to all {SessionCount} connected sessions",
            data.Length, sessions.Count);

        foreach (var session in sessions)
        {
            SendToSession(session, data);
        }
    }

    private void SendToSession(TerminalSession session, byte[] data)
    {
        try
        {
            if (session.Connection?.IsConnected == true && session.Connection.ShellStream != null)
            {
                session.Connection.ShellStream.Write(data, 0, data.Length);
                session.Connection.ShellStream.Flush();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send broadcast data to session {SessionId}", session.Id);
        }
    }
}
