namespace SshManager.Terminal.Services;

/// <summary>
/// Tracks which terminal control currently has keyboard focus.
/// This service provides a reliable way to determine if keyboard input
/// should be routed to a terminal vs handled by the application.
/// </summary>
public interface ITerminalFocusTracker
{
    /// <summary>
    /// Gets whether any terminal currently has keyboard focus.
    /// </summary>
    bool IsAnyTerminalFocused { get; }

    /// <summary>
    /// Gets the ID of the currently focused terminal session, or null if none.
    /// </summary>
    string? FocusedSessionId { get; }

    /// <summary>
    /// Event raised when terminal focus state changes.
    /// </summary>
    event Action<bool>? FocusChanged;

    /// <summary>
    /// Called by terminal controls when they gain focus.
    /// </summary>
    /// <param name="sessionId">The session ID of the terminal gaining focus.</param>
    void NotifyFocusGained(string sessionId);

    /// <summary>
    /// Called by terminal controls when they lose focus.
    /// </summary>
    /// <param name="sessionId">The session ID of the terminal losing focus.</param>
    void NotifyFocusLost(string sessionId);
}
