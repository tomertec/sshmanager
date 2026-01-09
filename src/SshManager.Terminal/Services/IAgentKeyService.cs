namespace SshManager.Terminal.Services;

/// <summary>
/// Service for managing SSH keys in SSH agents (Pageant and Windows OpenSSH Agent).
/// Provides functionality to add, remove, and list keys loaded in the active SSH agent.
/// </summary>
public interface IAgentKeyService
{
    /// <summary>
    /// Adds a private key file to the active SSH agent.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="passphrase">Optional passphrase if the key is encrypted.</param>
    /// <param name="lifetime">Optional key lifetime in the agent (null for unlimited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the operation including success status and any errors.</returns>
    Task<AgentKeyOperationResult> AddKeyToAgentAsync(
        string privateKeyPath,
        string? passphrase = null,
        TimeSpan? lifetime = null,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a private key from memory content to the active SSH agent.
    /// Useful after PPK conversion or when working with keys that aren't yet saved to disk.
    /// </summary>
    /// <param name="privateKeyContent">The private key content in PEM format.</param>
    /// <param name="passphrase">Optional passphrase if the key is encrypted.</param>
    /// <param name="lifetime">Optional key lifetime in the agent (null for unlimited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the operation including success status and any errors.</returns>
    Task<AgentKeyOperationResult> AddKeyContentToAgentAsync(
        string privateKeyContent,
        string? passphrase = null,
        TimeSpan? lifetime = null,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a specific key from the agent by public key path or fingerprint.
    /// </summary>
    /// <param name="publicKeyPathOrFingerprint">Path to the public key file (.pub) or the key fingerprint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the operation including success status and any errors.</returns>
    Task<AgentKeyOperationResult> RemoveKeyFromAgentAsync(
        string publicKeyPathOrFingerprint,
        CancellationToken ct = default);

    /// <summary>
    /// Removes all keys from the active SSH agent.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the operation including success status and any errors.</returns>
    Task<AgentKeyOperationResult> RemoveAllKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks which SSH agent is available and preferred.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about agent availability.</returns>
    Task<AgentAvailability> GetAgentAvailabilityAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents the result of an SSH agent key operation (add, remove, etc.).
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="AgentType">The agent type that was used ("Pageant", "OpenSSH Agent", or null).</param>
/// <param name="Fingerprint">The fingerprint of the key that was added/removed (if applicable).</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
public record AgentKeyOperationResult(
    bool Success,
    string? AgentType,
    string? Fingerprint,
    string? ErrorMessage);

/// <summary>
/// Represents the availability of SSH agents on the system.
/// </summary>
/// <param name="PageantAvailable">Whether Pageant is running and accessible.</param>
/// <param name="OpenSshAgentAvailable">Whether OpenSSH Agent service is running.</param>
/// <param name="PreferredAgent">The preferred agent to use ("Pageant", "OpenSSH Agent", or null if none).</param>
public record AgentAvailability(
    bool PageantAvailable,
    bool OpenSshAgentAvailable,
    string? PreferredAgent);
