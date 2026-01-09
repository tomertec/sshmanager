using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository for managing tracked SSH keys.
/// </summary>
public interface IManagedKeyRepository
{
    /// <summary>
    /// Gets all tracked SSH keys.
    /// </summary>
    Task<List<ManagedSshKey>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a tracked SSH key by ID.
    /// </summary>
    Task<ManagedSshKey?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets a tracked SSH key by its private key path.
    /// </summary>
    Task<ManagedSshKey?> GetByPathAsync(string privateKeyPath, CancellationToken ct = default);

    /// <summary>
    /// Adds a new tracked SSH key.
    /// </summary>
    Task AddAsync(ManagedSshKey key, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing tracked SSH key.
    /// </summary>
    Task UpdateAsync(ManagedSshKey key, CancellationToken ct = default);

    /// <summary>
    /// Deletes a tracked SSH key by ID.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Updates the last used timestamp for a key.
    /// </summary>
    Task UpdateLastUsedAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Checks if a key with the given path is already tracked.
    /// </summary>
    Task<bool> ExistsByPathAsync(string privateKeyPath, CancellationToken ct = default);
}
