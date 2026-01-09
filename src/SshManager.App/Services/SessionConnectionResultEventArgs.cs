using SshManager.Terminal;

namespace SshManager.App.Services;

/// <summary>
/// Event arguments for session connection completion events.
/// Provides information about the connection result including success status and any errors.
/// </summary>
public sealed class SessionConnectionResultEventArgs : EventArgs
{
    /// <summary>
    /// The terminal session that was being connected.
    /// </summary>
    public required TerminalSession Session { get; init; }

    /// <summary>
    /// Whether the connection was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The exception that occurred during connection, if any.
    /// Null if the connection was successful.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Optional error message describing the connection failure.
    /// Null if the connection was successful.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful connection result.
    /// </summary>
    /// <param name="session">The connected session.</param>
    /// <returns>Event args indicating successful connection.</returns>
    public static SessionConnectionResultEventArgs CreateSuccess(TerminalSession session)
    {
        return new SessionConnectionResultEventArgs
        {
            Session = session,
            Success = true,
            Exception = null,
            ErrorMessage = null
        };
    }

    /// <summary>
    /// Creates a failed connection result.
    /// </summary>
    /// <param name="session">The session that failed to connect.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>Event args indicating connection failure.</returns>
    public static SessionConnectionResultEventArgs CreateFailure(TerminalSession session, Exception exception)
    {
        return new SessionConnectionResultEventArgs
        {
            Session = session,
            Success = false,
            Exception = exception,
            ErrorMessage = exception.Message
        };
    }

    /// <summary>
    /// Creates a failed connection result with a custom error message.
    /// </summary>
    /// <param name="session">The session that failed to connect.</param>
    /// <param name="errorMessage">Custom error message.</param>
    /// <param name="exception">Optional exception that caused the failure.</param>
    /// <returns>Event args indicating connection failure.</returns>
    public static SessionConnectionResultEventArgs CreateFailure(
        TerminalSession session,
        string errorMessage,
        Exception? exception = null)
    {
        return new SessionConnectionResultEventArgs
        {
            Session = session,
            Success = false,
            Exception = exception,
            ErrorMessage = errorMessage
        };
    }
}
