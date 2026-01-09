using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing ProxyJump profiles.
/// </summary>
public interface IProxyJumpProfileRepository
{
    /// <summary>
    /// Gets all ProxyJump profiles.
    /// </summary>
    Task<IReadOnlyList<ProxyJumpProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a ProxyJump profile by ID without navigation properties.
    /// </summary>
    Task<ProxyJumpProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets a ProxyJump profile by ID with all jump hops loaded.
    /// </summary>
    Task<ProxyJumpProfile?> GetByIdWithHopsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new ProxyJump profile.
    /// </summary>
    Task<ProxyJumpProfile> AddAsync(ProxyJumpProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing ProxyJump profile.
    /// </summary>
    Task UpdateAsync(ProxyJumpProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Deletes a ProxyJump profile by ID.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Searches profiles by display name or description.
    /// </summary>
    Task<IReadOnlyList<ProxyJumpProfile>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Reorders the hops in a profile.
    /// </summary>
    /// <param name="profileId">The profile to reorder hops for.</param>
    /// <param name="hopIdsInOrder">The hop IDs in the desired order.</param>
    Task ReorderHopsAsync(Guid profileId, IEnumerable<Guid> hopIdsInOrder, CancellationToken ct = default);
}
