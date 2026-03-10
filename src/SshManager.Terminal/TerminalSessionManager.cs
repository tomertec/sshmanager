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

    /// <summary>
    /// Guards all reads and writes to <see cref="Sessions"/> that originate from
    /// non-UI threads. Mutations that run inside a <c>Dispatcher.InvokeAsync</c>
    /// callback already execute serially on the UI thread, so they implicitly
    /// have exclusive access while still holding this lock via the outer scope.
    /// </summary>
    private readonly object _sessionsLock = new();

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

    public int BroadcastSelectedCount
    {
        get
        {
            lock (_sessionsLock)
            {
                return Sessions.Count(s => s.IsSelectedForBroadcast);
            }
        }
    }

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

        lock (_sessionsLock)
        {
            Sessions.Add(session);
        }

        CurrentSession = session;

        _logger.LogInformation("Created terminal session {SessionId} with title '{Title}'", session.Id, title);
        SessionCreated?.Invoke(this, session);
        return session;
    }

    public async Task CloseSessionAsync(Guid sessionId)
    {
        TerminalSession? session;
        lock (_sessionsLock)
        {
            session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        }

        if (session != null)
        {
            _logger.LogInformation("Closing session {SessionId}", sessionId);
            await session.CloseAsync();
        }
        else
        {
            _logger.LogWarning("Attempted to close non-existent session {SessionId}", sessionId);
        }
    }

    public async Task CloseAllSessionsAsync()
    {
        List<TerminalSession> sessionsToClose;
        lock (_sessionsLock)
        {
            _logger.LogInformation("Closing all {SessionCount} terminal sessions", Sessions.Count);
            // Snapshot under lock to avoid modifying collection while iterating
            sessionsToClose = Sessions.ToList();
        }

        foreach (var session in sessionsToClose)
        {
            await session.CloseAsync();
        }

        _logger.LogDebug("All terminal sessions closed");
    }

    private void OnSessionClosed(object? sender, EventArgs e)
    {
        if (sender is not TerminalSession session) return;

        session.SessionClosed -= OnSessionClosed;

        // ObservableCollection must be modified on the UI thread.
        // The lock inside the dispatch callback serializes with other _sessionsLock holders.
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TerminalSession? next = null;

            lock (_sessionsLock)
            {
                Sessions.Remove(session);

                _logger.LogDebug("Session {SessionId} removed from active sessions", session.Id);

                // Determine next session while holding the lock so the collection is stable.
                if (CurrentSession == session)
                {
                    next = Sessions.FirstOrDefault();
                }
            }

            // Apply CurrentSession update outside the lock — it fires CurrentSessionChanged
            // which may invoke subscriber callbacks that also acquire _sessionsLock.
            if (CurrentSession == session)
            {
                CurrentSession = next;
            }

            // Raise SessionClosed outside the lock to prevent subscriber deadlocks.
            SessionClosed?.Invoke(this, session);
        });
    }

    public IEnumerable<TerminalSession> GetBroadcastSessions()
    {
        lock (_sessionsLock)
        {
            return Sessions.Where(s => s.IsSelectedForBroadcast && s.IsConnected).ToList();
        }
    }

    public void ToggleBroadcastSelection(TerminalSession session)
    {
        session.IsSelectedForBroadcast = !session.IsSelectedForBroadcast;
        _logger.LogDebug("Session {SessionId} broadcast selection toggled to {IsSelected}",
            session.Id, session.IsSelectedForBroadcast);
    }

    public void SelectAllForBroadcast()
    {
        List<TerminalSession> snapshot;
        lock (_sessionsLock)
        {
            snapshot = Sessions.Where(s => s.IsConnected).ToList();
        }

        foreach (var session in snapshot)
        {
            session.IsSelectedForBroadcast = true;
        }

        _logger.LogDebug("All connected sessions selected for broadcast");
    }

    public void DeselectAllForBroadcast()
    {
        List<TerminalSession> snapshot;
        lock (_sessionsLock)
        {
            snapshot = Sessions.ToList();
        }

        foreach (var session in snapshot)
        {
            session.IsSelectedForBroadcast = false;
        }

        _logger.LogDebug("All sessions deselected from broadcast");
    }
}
