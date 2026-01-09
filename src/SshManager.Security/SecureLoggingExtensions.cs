using Microsoft.Extensions.Logging;
using SshManager.Core.Models;

namespace SshManager.Security;

/// <summary>
/// Provides extension methods for secure logging that prevents accidental credential exposure.
/// </summary>
public static class SecureLoggingExtensions
{
    /// <summary>
    /// Logs authentication attempt without exposing credential details.
    /// </summary>
    public static void LogAuthenticationAttempt(
        this ILogger logger,
        string hostname,
        string username,
        AuthType authType)
    {
        logger.LogInformation(
            "Initiating SSH connection to {Host} as {User} using {AuthType}",
            hostname,
            MaskUsername(username),
            authType);
    }

    /// <summary>
    /// Masks username for logging - shows first char and length only.
    /// </summary>
    private static string MaskUsername(string username)
    {
        if (string.IsNullOrEmpty(username) || username.Length <= 1)
            return "***";

        return $"{username[0]}***({username.Length} chars)";
    }
}
