using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing tunnel profile configurations.
/// </summary>
public interface ITunnelProfileRepository
{
    /// <summary>
    /// Retrieves all tunnel profiles, ordered by display name.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of all tunnel profiles with their nodes and edges.</returns>
    Task<List<TunnelProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific tunnel profile by ID, including nodes and edges.
    /// </summary>
    /// <param name="id">The unique identifier of the tunnel profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tunnel profile if found; otherwise, null.</returns>
    Task<TunnelProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new tunnel profile to the repository.
    /// </summary>
    /// <param name="profile">The tunnel profile to add.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(TunnelProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing tunnel profile.
    /// </summary>
    /// <param name="profile">The tunnel profile with updated values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(TunnelProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Deletes a tunnel profile and all associated nodes and edges.
    /// </summary>
    /// <param name="id">The unique identifier of the tunnel profile to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
