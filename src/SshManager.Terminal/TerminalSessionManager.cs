using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal;

/// <summary>
/// Manages active terminal sessions.
/// </summary>
public sealed class TerminalSessionManager : ITerminalSessionManager
{
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ObservableCollection<TerminalSession> Sessions { get; } = [];

    private TerminalSession? _currentSession;
    public TerminalSession? CurrentSession
    {
        get => _currentSession;
        set
        {
            if (_currentSession != value)
            {
                _currentSession = value;
                _logger.LogDebug("Current session changed to {SessionId}", value?.Id);
                CurrentSessionChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<TerminalSession>? SessionCreated;
    public event EventHandler<TerminalSession>? SessionClosed;
    public event EventHandler<TerminalSession?>? CurrentSessionChanged;
    public event EventHandler<bool>? BroadcastModeChanged;

    private bool _isBroadcastMode;
    public bool IsBroadcastMode
    {
        get => _isBroadcastMode;
        set
        {
            if (_isBroadcastMode != value)
            {
                _isBroadcastMode = value;
                _logger.LogInformation("Broadcast mode {State}", value ? "enabled" : "disabled");

                // When disabling broadcast mode, deselect all sessions
                if (!value)
                {
                    DeselectAllForBroadcast();
                }
                else
                {
                    // When enabling, auto-select current session
                    if (CurrentSession != null)
                    {
                        CurrentSession.IsSelectedForBroadcast = true;
                    }
                }

                BroadcastModeChanged?.Invoke(this, value);
            }
        }
    }

    public int BroadcastSelectedCount => Sessions.Count(s => s.IsSelectedForBroadcast);

    public TerminalSessionManager(ILogger<TerminalSessionManager>? logger = null, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger ?? NullLogger<TerminalSessionManager>.Instance;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public TerminalSession CreateSession(string title)
    {
        var sessionLogger = _loggerFactory.CreateLogger<TerminalSession>();
        var session = new TerminalSession(sessionLogger) { Title = title };
        session.SessionClosed += OnSessionClosed;

        Sessions.Add(session);
        CurrentSession = session;

        _logger.LogInformation("Created terminal session {SessionId} with title '{Title}'", session.Id, title);
        SessionCreated?.Invoke(this, session);
        return session;
    }

    public void CloseSession(Guid sessionId)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session != null)
        {
            _logger.LogInformation("Closing session {SessionId}", sessionId);
            session.Close();
        }
        else
        {
            _logger.LogWarning("Attempted to close non-existent session {SessionId}", sessionId);
        }
    }

    public void CloseAllSessions()
    {
        _logger.LogInformation("Closing all {SessionCount} terminal sessions", Sessions.Count);
        // Create a copy to avoid modifying collection while iterating
        var sessionsToClose = Sessions.ToList();
        foreach (var session in sessionsToClose)
        {
            session.Close();
        }
        _logger.LogDebug("All terminal sessions closed");
    }

    private void OnSessionClosed(object? sender, EventArgs e)
    {
        if (sender is not TerminalSession session) return;

        session.SessionClosed -= OnSessionClosed;
        Sessions.Remove(session);

        _logger.LogDebug("Session {SessionId} removed from active sessions", session.Id);

        // Select next session if current was closed
        if (CurrentSession == session)
        {
            CurrentSession = Sessions.FirstOrDefault();
        }

        SessionClosed?.Invoke(this, session);
    }

    public IEnumerable<TerminalSession> GetBroadcastSessions()
    {
        return Sessions.Where(s => s.IsSelectedForBroadcast && s.IsConnected);
    }

    public void ToggleBroadcastSelection(TerminalSession session)
    {
        session.IsSelectedForBroadcast = !session.IsSelectedForBroadcast;
        _logger.LogDebug("Session {SessionId} broadcast selection toggled to {IsSelected}",
            session.Id, session.IsSelectedForBroadcast);
    }

    public void SelectAllForBroadcast()
    {
        foreach (var session in Sessions.Where(s => s.IsConnected))
        {
            session.IsSelectedForBroadcast = true;
        }
        _logger.LogDebug("All connected sessions selected for broadcast");
    }

    public void DeselectAllForBroadcast()
    {
        foreach (var session in Sessions)
        {
            session.IsSelectedForBroadcast = false;
        }
        _logger.LogDebug("All sessions deselected from broadcast");
    }
}
