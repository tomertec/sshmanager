using System.Collections.ObjectModel;

namespace SshManager.Terminal;

/// <summary>
/// Manages terminal sessions.
/// </summary>
public interface ITerminalSessionManager
{
    /// <summary>
    /// Active terminal sessions.
    /// </summary>
    ObservableCollection<TerminalSession> Sessions { get; }

    /// <summary>
    /// The currently selected/focused session.
    /// </summary>
    TerminalSession? CurrentSession { get; set; }

    /// <summary>
    /// Event raised when a new session is created.
    /// </summary>
    event EventHandler<TerminalSession>? SessionCreated;

    /// <summary>
    /// Event raised when a session is closed.
    /// </summary>
    event EventHandler<TerminalSession>? SessionClosed;

    /// <summary>
    /// Event raised when the current session changes.
    /// </summary>
    event EventHandler<TerminalSession?>? CurrentSessionChanged;

    /// <summary>
    /// Creates a new terminal session.
    /// </summary>
    TerminalSession CreateSession(string title);

    /// <summary>
    /// Closes a specific session asynchronously.
    /// </summary>
    Task CloseSessionAsync(Guid sessionId);

    /// <summary>
    /// Closes all sessions asynchronously.
    /// </summary>
    Task CloseAllSessionsAsync();

    /// <summary>
    /// Whether broadcast input mode is enabled.
    /// When enabled, keyboard input is sent to all selected sessions.
    /// </summary>
    bool IsBroadcastMode { get; set; }

    /// <summary>
    /// Event raised when broadcast mode changes.
    /// </summary>
    event EventHandler<bool>? BroadcastModeChanged;

    /// <summary>
    /// Gets the sessions selected for broadcast input.
    /// </summary>
    IEnumerable<TerminalSession> GetBroadcastSessions();

    /// <summary>
    /// Toggles a session's selection for broadcast input.
    /// </summary>
    void ToggleBroadcastSelection(TerminalSession session);

    /// <summary>
    /// Selects all connected sessions for broadcast input.
    /// </summary>
    void SelectAllForBroadcast();

    /// <summary>
    /// Deselects all sessions from broadcast input.
    /// </summary>
    void DeselectAllForBroadcast();

    /// <summary>
    /// Gets the count of sessions selected for broadcast.
    /// </summary>
    int BroadcastSelectedCount { get; }
}
