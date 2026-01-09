using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Result of validating a ProxyJump profile.
/// </summary>
public sealed class ProxyJumpValidationResult
{
    /// <summary>
    /// Whether the profile is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// List of specific issues found during validation.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ProxyJumpValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with the given error.
    /// </summary>
    public static ProxyJumpValidationResult Failure(string errorMessage, IReadOnlyList<string>? issues = null) =>
        new()
        {
            IsValid = false,
            ErrorMessage = errorMessage,
            Issues = issues ?? []
        };
}

/// <summary>
/// Service for resolving and validating ProxyJump connection chains.
/// </summary>
public interface IProxyJumpService
{
    /// <summary>
    /// Resolves the complete connection chain for a host.
    /// Returns an ordered list of TerminalConnectionInfo for each hop, ending with the target host.
    /// </summary>
    /// <param name="targetHost">The final destination host.</param>
    /// <param name="decryptedPasswords">Dictionary mapping host IDs to their decrypted passwords.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Ordered list of connection info objects:
    /// - First entry is the first jump host
    /// - Last entry is the target host
    /// - Empty list if no proxy chain is configured
    /// </returns>
    Task<IReadOnlyList<TerminalConnectionInfo>> ResolveConnectionChainAsync(
        HostEntry targetHost,
        IReadOnlyDictionary<Guid, string>? decryptedPasswords = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a ProxyJump profile for correctness.
    /// Checks for:
    /// - Circular references (host referencing itself in chain)
    /// - Missing or disabled jump hosts
    /// - Empty hop chain
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with any errors found.</returns>
    Task<ProxyJumpValidationResult> ValidateProfileAsync(
        ProxyJumpProfile profile,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a ProxyJump profile for a specific target host.
    /// Additional checks:
    /// - Target host is not part of its own jump chain
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <param name="targetHostId">The target host that will use this profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with any errors found.</returns>
    Task<ProxyJumpValidationResult> ValidateProfileForHostAsync(
        ProxyJumpProfile profile,
        Guid targetHostId,
        CancellationToken ct = default);

    /// <summary>
    /// Builds a display string representing the connection chain.
    /// Example: "You → bastion → internal-jump → [Target]"
    /// </summary>
    /// <param name="profile">The profile to describe.</param>
    /// <param name="targetHostName">Optional target host name for the chain end.</param>
    /// <returns>Human-readable chain description.</returns>
    string BuildChainDisplayString(ProxyJumpProfile profile, string? targetHostName = null);
}
