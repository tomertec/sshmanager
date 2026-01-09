using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository for managing SSH host fingerprints.
/// </summary>
public interface IHostFingerprintRepository
{
    /// <summary>
    /// Gets the fingerprint for a specific host.
    /// </summary>
    Task<HostFingerprint?> GetByHostAsync(Guid hostId, CancellationToken ct = default);

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
