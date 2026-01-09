using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing host profiles.
/// </summary>
public interface IHostProfileRepository
{
    Task<List<HostProfile>> GetAllAsync(CancellationToken ct = default);
    Task<HostProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(HostProfile profile, CancellationToken ct = default);
    Task UpdateAsync(HostProfile profile, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
