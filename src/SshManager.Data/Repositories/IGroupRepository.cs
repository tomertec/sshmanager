using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing host groups.
/// </summary>
public interface IGroupRepository
{
    Task<List<HostGroup>> GetAllAsync(CancellationToken ct = default);
    Task<HostGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(HostGroup group, CancellationToken ct = default);
    Task UpdateAsync(HostGroup group, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task ReorderAsync(List<Guid> orderedIds, CancellationToken ct = default);
}
