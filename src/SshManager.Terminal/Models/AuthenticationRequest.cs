namespace SshManager.Terminal.Models;

/// <summary>
/// Represents a keyboard-interactive authentication request containing one or more prompts.
/// Used for 2FA/TOTP and other multi-factor authentication scenarios.
/// </summary>
public sealed class AuthenticationRequest
{
    /// <summary>
    /// The name of the authentication method (may be empty).
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Instructions to display to the user (may be empty).
    /// </summary>
    public string Instruction { get; init; } = "";

    /// <summary>
    /// The list of prompts that require user responses.
    /// </summary>
    public required IReadOnlyList<AuthenticationPrompt> Prompts { get; init; }
}
