namespace SshManager.Core.Exceptions;

/// <summary>
/// Exception thrown when SSH authentication fails.
/// Provides details about the authentication method and failure type.
/// </summary>
public class AuthenticationException : SshManagerException
{
    /// <summary>
    /// Gets the authentication method that was attempted.
    /// </summary>
    public string? AuthMethod { get; }

    /// <summary>
    /// Gets the username that was used for authentication.
    /// </summary>
    public string? Username { get; }

    /// <summary>
    /// Gets whether the private key file could not be loaded.
    /// </summary>
    public bool IsKeyFileError { get; }

    /// <summary>
    /// Gets whether the passphrase was incorrect.
    /// </summary>
    public bool IsPassphraseError { get; }

    /// <summary>
    /// Creates a new AuthenticationException.
    /// </summary>
    /// <param name="message">Technical error message.</param>
    /// <param name="username">The username that was used.</param>
    /// <param name="authMethod">The authentication method (e.g., "password", "publickey").</param>
    /// <param name="isKeyFileError">Whether this is a key file loading error.</param>
    /// <param name="isPassphraseError">Whether the passphrase was incorrect.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public AuthenticationException(
        string message,
        string? username = null,
        string? authMethod = null,
        bool isKeyFileError = false,
        bool isPassphraseError = false,
        Exception? innerException = null)
        : base(
            message,
            GetUserFriendlyMessage(authMethod, isKeyFileError, isPassphraseError),
            "AUTH_FAILED",
            innerException)
    {
        AuthMethod = authMethod;
        Username = username;
        IsKeyFileError = isKeyFileError;
        IsPassphraseError = isPassphraseError;
    }

    private static string GetUserFriendlyMessage(string? authMethod, bool isKeyFileError, bool isPassphraseError)
    {
        if (isPassphraseError)
        {
            return "The passphrase for the private key is incorrect.";
        }

        if (isKeyFileError)
        {
            return "Could not load the private key file. Check that the file exists and is in a valid format.";
        }

        return authMethod?.ToLowerInvariant() switch
        {
            "password" => "Password authentication failed. Check your password and try again.",
            "publickey" => "Public key authentication failed. The server may not accept this key.",
            "keyboard-interactive" => "Keyboard-interactive authentication failed.",
            _ => "Authentication failed. Check your credentials and try again."
        };
    }

    /// <summary>
    /// Creates an AuthenticationException for a key file loading error.
    /// </summary>
    public static AuthenticationException KeyFileError(string keyPath, Exception? innerException = null)
    {
        return new AuthenticationException(
            $"Failed to load private key from: {keyPath}",
            authMethod: "publickey",
            isKeyFileError: true,
            innerException: innerException);
    }

    /// <summary>
    /// Creates an AuthenticationException for an incorrect passphrase.
    /// </summary>
    public static AuthenticationException WrongPassphrase(Exception? innerException = null)
    {
        return new AuthenticationException(
            "Private key passphrase is incorrect",
            authMethod: "publickey",
            isPassphraseError: true,
            innerException: innerException);
    }

    /// <summary>
    /// Creates an AuthenticationException for password authentication failure.
    /// </summary>
    public static AuthenticationException PasswordFailed(string? username = null, Exception? innerException = null)
    {
        return new AuthenticationException(
            "Password authentication failed",
            username: username,
            authMethod: "password",
            innerException: innerException);
    }
}
