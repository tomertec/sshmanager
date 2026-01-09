using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing command snippets.
/// </summary>
public interface ISnippetRepository
{
    Task<List<CommandSnippet>> GetAllAsync(CancellationToken ct = default);
    Task<List<CommandSnippet>> GetByCategoryAsync(string? category, CancellationToken ct = default);
    Task<List<CommandSnippet>> SearchAsync(string searchTerm, CancellationToken ct = default);
    Task<List<string>> GetCategoriesAsync(CancellationToken ct = default);
    Task<CommandSnippet?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(CommandSnippet snippet, CancellationToken ct = default);
    Task UpdateAsync(CommandSnippet snippet, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
