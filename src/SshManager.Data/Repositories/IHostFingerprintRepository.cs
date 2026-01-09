using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository for managing SSH host fingerprints.
/// Supports multiple key algorithms per host (RSA, ED25519, ECDSA, etc.).
/// </summary>
public interface IHostFingerprintRepository
{
    /// <summary>
    /// Gets the first fingerprint for a specific host (any algorithm).
    /// For algorithm-specific lookup, use <see cref="GetByHostAndAlgorithmAsync"/>.
    /// </summary>
    Task<HostFingerprint?> GetByHostAsync(Guid hostId, CancellationToken ct = default);

    /// <summary>
    /// Gets the fingerprint for a specific host and algorithm combination.
    /// Use this for proper multi-algorithm support during connection verification.
    /// </summary>
    /// <param name="hostId">The host ID.</param>
    /// <param name="algorithm">The key algorithm (e.g., "ssh-rsa", "ssh-ed25519", "ecdsa-sha2-nistp256").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fingerprint for this host/algorithm pair, or null if not found.</returns>
    Task<HostFingerprint?> GetByHostAndAlgorithmAsync(Guid hostId, string algorithm, CancellationToken ct = default);

    /// <summary>
    /// Gets all fingerprints for a specific host (all algorithms).
    /// </summary>
    /// <param name="hostId">The host ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of all fingerprints stored for this host.</returns>
    Task<IReadOnlyList<HostFingerprint>> GetAllByHostAsync(Guid hostId, CancellationToken ct = default);

    /// <summary>
    /// Gets all fingerprints.
    /// </summary>
    Task<IReadOnlyList<HostFingerprint>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new fingerprint.
    /// </summary>
    Task AddAsync(HostFingerprint fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Updates the last seen timestamp for a fingerprint.
    /// </summary>
    Task UpdateLastSeenAsync(Guid fingerprintId, CancellationToken ct = default);

    /// <summary>
    /// Updates the fingerprint (e.g., when it changes and user accepts).
    /// </summary>
    Task UpdateAsync(HostFingerprint fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Deletes a fingerprint.
    /// </summary>
    Task DeleteAsync(Guid fingerprintId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all fingerprints for a host.
    /// </summary>
    Task DeleteByHostAsync(Guid hostId, CancellationToken ct = default);
}
