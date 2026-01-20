namespace SshManager.Core.Exceptions;

/// <summary>
/// Base exception class for all SshManager-specific exceptions.
/// Provides a consistent exception hierarchy with user-friendly messages and error codes.
/// </summary>
public class SshManagerException : Exception
{
    /// <summary>
    /// Gets the error code for this exception, useful for programmatic handling.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets a user-friendly message suitable for display in the UI.
    /// </summary>
    public string UserFriendlyMessage { get; }

    /// <summary>
    /// Creates a new SshManagerException.
    /// </summary>
    /// <param name="message">Technical error message.</param>
    /// <param name="userFriendlyMessage">User-friendly message for UI display.</param>
    /// <param name="errorCode">Optional error code for programmatic handling.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public SshManagerException(
        string message,
        string? userFriendlyMessage = null,
        string? errorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        UserFriendlyMessage = userFriendlyMessage ?? message;
    }
}
