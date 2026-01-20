namespace SshManager.Core.Models;

/// <summary>
/// Connection statistics for a host entry.
/// </summary>
/// <param name="LastConnected">When the host was last connected to.</param>
/// <param name="TotalConnections">Total number of connection attempts.</param>
/// <param name="SuccessfulConnections">Number of successful connections.</param>
/// <param name="SuccessRate">Success rate as a percentage (0-100).</param>
public record HostConnectionStats(
    DateTimeOffset? LastConnected,
    int TotalConnections,
    int SuccessfulConnections,
    double SuccessRate
);
