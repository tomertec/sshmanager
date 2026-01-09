using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing host environment variables.
/// </summary>
public interface IHostEnvironmentVariableRepository
{
    /// <summary>
    /// Gets all environment variables for a specific host.
    /// </summary>
    /// <param name="hostId">The host entry ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of environment variables ordered by SortOrder.</returns>
    Task<List<HostEnvironmentVariable>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default);

    /// <summary>
    /// Replaces all environment variables for a host with a new set.
    /// </summary>
    /// <param name="hostId">The host entry ID.</param>
    /// <param name="vars">The new set of environment variables.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetForHostAsync(Guid hostId, IEnumerable<HostEnvironmentVariable> vars, CancellationToken ct = default);

    /// <summary>
    /// Adds a new environment variable.
    /// </summary>
    /// <param name="variable">The environment variable to add.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(HostEnvironmentVariable variable, CancellationToken ct = default);

    /// <summary>
    /// Deletes an environment variable by ID.
    /// </summary>
    /// <param name="id">The environment variable ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
