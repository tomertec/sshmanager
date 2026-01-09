using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing port forwarding profiles.
/// </summary>
public interface IPortForwardingProfileRepository
{
    /// <summary>
    /// Gets all port forwarding profiles.
    /// </summary>
    Task<IReadOnlyList<PortForwardingProfile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all port forwarding profiles associated with a specific host.
    /// </summary>
    Task<IReadOnlyList<PortForwardingProfile>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default);

    /// <summary>
    /// Gets all global port forwarding profiles (not associated with any host).
    /// </summary>
    Task<IReadOnlyList<PortForwardingProfile>> GetGlobalProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a port forwarding profile by ID.
    /// </summary>
    Task<PortForwardingProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new port forwarding profile.
    /// </summary>
    Task<PortForwardingProfile> AddAsync(PortForwardingProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing port forwarding profile.
    /// </summary>
    Task UpdateAsync(PortForwardingProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Deletes a port forwarding profile by ID.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Checks if a local port is already in use by another profile.
    /// </summary>
    /// <param name="localPort">The local port to check.</param>
    /// <param name="excludeId">Optional profile ID to exclude from the check (for updates).</param>
    /// <returns>True if the port is in use by another profile.</returns>
    Task<bool> IsPortInUseAsync(int localPort, Guid? excludeId = null, CancellationToken ct = default);
}
