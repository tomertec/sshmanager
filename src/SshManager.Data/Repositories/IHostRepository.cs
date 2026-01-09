using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing host entries.
/// </summary>
public interface IHostRepository
{
    Task<List<HostEntry>> GetAllAsync(CancellationToken ct = default);
    Task<List<HostEntry>> GetByGroupAsync(Guid? groupId, CancellationToken ct = default);
    Task<List<HostEntry>> SearchAsync(string searchTerm, CancellationToken ct = default);
    Task<HostEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(HostEntry host, CancellationToken ct = default);
    Task UpdateAsync(HostEntry host, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
