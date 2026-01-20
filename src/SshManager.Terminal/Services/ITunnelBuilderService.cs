using SshManager.Core.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service interface for building and executing SSH tunnel chains from visual profiles.
/// </summary>
public interface ITunnelBuilderService
{
    /// <summary>
    /// Validates a tunnel profile graph to ensure it's structurally sound and executable.
    /// </summary>
    /// <param name="profile">The tunnel profile to validate.</param>
    /// <returns>A validation result containing any errors or warnings.</returns>
    /// <remarks>
    /// Validation checks include:
    /// - Profile has at least 2 nodes (source and target)
    /// - No circular dependencies in the edge graph
    /// - Port numbers are valid (1-65535)
    /// - SshHost nodes have valid HostId references
    /// - LocalMachine node exists and is the root of the graph
    /// - All edges connect valid nodes
    /// - PortForward and SocksProxy nodes have required port configurations
    /// </remarks>
    TunnelValidationResult Validate(TunnelProfile profile);

    /// <summary>
    /// Generates an equivalent SSH command line for the tunnel configuration.
    /// </summary>
    /// <param name="profile">The tunnel profile to generate a command for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted SSH command string with -L, -R, -D, and -J flags.</returns>
    /// <remarks>
    /// The generated command uses:
    /// - `-L` for local port forwarding
    /// - `-R` for remote port forwarding
    /// - `-D` for dynamic (SOCKS) forwarding
    /// - `-J` for ProxyJump (multi-hop tunnels)
    /// </remarks>
    Task<string> GenerateSshCommandAsync(TunnelProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Executes the tunnel chain by establishing SSH connections and setting up port forwarding.
    /// </summary>
    /// <param name="profile">The tunnel profile to execute.</param>
    /// <param name="hostKeyCallback">Callback for verifying SSH host keys. Required for secure connections.</param>
    /// <param name="kbInteractiveCallback">Optional callback for keyboard-interactive authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An execution result indicating success or failure.</returns>
    /// <remarks>
    /// Execution steps:
    /// 1. Validate the profile
    /// 2. Establish SSH connections in topological order
    /// 3. Set up port forwarding using SSH.NET ForwardedPort classes
    /// 4. Track active tunnels for status monitoring
    ///
    /// SECURITY NOTE: If hostKeyCallback is null and SkipHostKeyVerification is not enabled
    /// on the host, connections will be rejected to prevent man-in-the-middle attacks.
    /// </remarks>
    Task<TunnelExecutionResult> ExecuteAsync(
        TunnelProfile profile,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback = null,
        CancellationToken ct = default);

    /// <summary>
    /// Stops an active tunnel and cleans up all associated resources.
    /// </summary>
    /// <param name="profileId">The profile ID of the tunnel to stop.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopAsync(Guid profileId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of all active tunnels.
    /// </summary>
    /// <returns>A read-only dictionary of active tunnel statuses keyed by profile ID.</returns>
    IReadOnlyDictionary<Guid, TunnelStatus> GetActiveTunnels();
}

/// <summary>
/// Result of tunnel profile validation.
/// </summary>
/// <param name="IsValid">True if the profile is valid and can be executed.</param>
/// <param name="Errors">Collection of error messages preventing execution.</param>
/// <param name="Warnings">Collection of warning messages about potential issues.</param>
public record TunnelValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Result of tunnel execution.
/// </summary>
/// <param name="Success">True if the tunnel was successfully established.</param>
/// <param name="ErrorMessage">Error message if execution failed.</param>
/// <param name="SessionId">Session ID for tracking the active tunnel.</param>
public record TunnelExecutionResult(
    bool Success,
    string? ErrorMessage,
    Guid? SessionId);

/// <summary>
/// Status of an active tunnel.
/// </summary>
/// <param name="ProfileId">The profile ID of the tunnel.</param>
/// <param name="DisplayName">User-friendly display name.</param>
/// <param name="StartedAt">When the tunnel was started.</param>
/// <param name="IsActive">Whether the tunnel is currently active.</param>
public record TunnelStatus(
    Guid ProfileId,
    string DisplayName,
    DateTimeOffset StartedAt,
    bool IsActive);
