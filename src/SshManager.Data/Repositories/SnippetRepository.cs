using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing command snippets.
/// </summary>
public sealed class SnippetRepository : ISnippetRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SnippetRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<CommandSnippet>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Snippets
            .OrderBy(s => s.Category ?? "")
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<List<CommandSnippet>> GetByCategoryAsync(string? category, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Snippets
            .Where(s => s.Category == category)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<List<CommandSnippet>> SearchAsync(string searchTerm, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var term = searchTerm.ToLowerInvariant();
        return await db.Snippets
            .Where(s =>
                s.Name.ToLower().Contains(term) ||
                s.Command.ToLower().Contains(term) ||
                (s.Description != null && s.Description.ToLower().Contains(term)) ||
                (s.Category != null && s.Category.ToLower().Contains(term)))
            .OrderBy(s => s.Category ?? "")
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<List<string>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Snippets
            .Where(s => s.Category != null)
            .Select(s => s.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    public async Task<CommandSnippet?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Snippets.FindAsync([id], ct);
    }

    public async Task AddAsync(CommandSnippet snippet, CancellationToken ct = default)
    {
        snippet.CreatedAt = DateTimeOffset.UtcNow;
        snippet.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Set sort order to be last within the category
        var maxOrder = await db.Snippets
            .Where(s => s.Category == snippet.Category)
            .MaxAsync(s => (int?)s.SortOrder, ct) ?? -1;
        snippet.SortOrder = maxOrder + 1;

        db.Snippets.Add(snippet);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CommandSnippet snippet, CancellationToken ct = default)
    {
        snippet.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Snippets.FindAsync([snippet.Id], ct);
        if (existing == null)
            return;

        db.Entry(existing).CurrentValues.SetValues(snippet);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snippet = await db.Snippets.FindAsync([id], ct);
        if (snippet != null)
        {
            db.Snippets.Remove(snippet);
            await db.SaveChangesAsync(ct);
        }
    }
}
