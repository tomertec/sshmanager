namespace SshManager.Terminal.Services;

/// <summary>
/// Provides diagnostic information about SSH agent availability and loaded keys.
/// Supports both Pageant and OpenSSH Agent on Windows.
/// </summary>
public interface IAgentDiagnosticsService
{
    /// <summary>
    /// Gets whether Pageant (PuTTY's SSH agent) is currently available.
    /// </summary>
    bool IsPageantAvailable { get; }

    /// <summary>
    /// Gets whether OpenSSH Agent is currently available (Windows named pipe or Unix socket).
    /// </summary>
    bool IsOpenSshAgentAvailable { get; }

    /// <summary>
    /// Gets the type of the active SSH agent ("Pageant", "OpenSSH Agent", or null if none).
    /// </summary>
    string? ActiveAgentType { get; }

    /// <summary>
    /// Gets the number of keys currently loaded in the active SSH agent.
    /// </summary>
    int AvailableKeyCount { get; }

    /// <summary>
    /// Performs a full diagnostic scan of available SSH agents and their loaded keys.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Comprehensive diagnostic information about SSH agent state.</returns>
    Task<AgentDiagnosticResult> GetDiagnosticsAsync(CancellationToken ct = default);

    /// <summary>
    /// Refreshes the cached diagnostic information by re-scanning available agents.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents the result of an SSH agent diagnostic scan.
/// </summary>
/// <param name="PageantAvailable">Whether Pageant is available.</param>
/// <param name="OpenSshAgentAvailable">Whether OpenSSH Agent is available.</param>
/// <param name="ActiveAgentType">The type of the active agent ("Pageant", "OpenSSH Agent", or null).</param>
/// <param name="Keys">List of keys loaded in the active agent.</param>
/// <param name="ErrorMessage">Error message if diagnostic scan failed.</param>
public record AgentDiagnosticResult(
    bool PageantAvailable,
    bool OpenSshAgentAvailable,
    string? ActiveAgentType,
    IReadOnlyList<AgentKeyInfo> Keys,
    string? ErrorMessage);

/// <summary>
/// Represents information about a single SSH key loaded in an agent.
/// </summary>
/// <param name="Fingerprint">SSH key fingerprint (SHA-256 hash in base64 format).</param>
/// <param name="KeyType">Key algorithm type (e.g., "ssh-rsa", "ssh-ed25519", "ecdsa-sha2-nistp256").</param>
/// <param name="Comment">Optional comment/label associated with the key.</param>
/// <param name="KeySizeBits">Key size in bits (e.g., 2048, 4096 for RSA; 256 for Ed25519).</param>
public record AgentKeyInfo(
    string Fingerprint,
    string KeyType,
    string? Comment,
    int KeySizeBits);
