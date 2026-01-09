namespace SshManager.Terminal.Models;

/// <summary>
/// Represents a single prompt in a keyboard-interactive authentication sequence.
/// Used for 2FA/TOTP and other multi-factor authentication scenarios.
/// </summary>
public sealed class AuthenticationPrompt
{
    /// <summary>
    /// The prompt text to display to the user (e.g., "Password:", "Verification code:").
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Whether the input should be masked (like a password field).
    /// </summary>
    public bool IsPassword { get; init; }

    /// <summary>
    /// The user's response to this prompt. Set after the user provides input.
    /// </summary>
    public string? Response { get; set; }
}
