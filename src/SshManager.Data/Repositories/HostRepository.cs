using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing host entries.
/// </summary>
public sealed class HostRepository : IHostRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HostRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<HostEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts
            .Include(h => h.Group)
            .OrderBy(h => h.Group != null ? h.Group.SortOrder : int.MaxValue)
            .ThenBy(h => h.SortOrder)
            .ThenBy(h => h.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<List<HostEntry>> GetByGroupAsync(Guid? groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts
            .Where(h => h.GroupId == groupId)
            .OrderBy(h => h.SortOrder)
            .ThenBy(h => h.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<List<HostEntry>> SearchAsync(string searchTerm, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return await GetAllAsync(ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var term = searchTerm.ToLowerInvariant();

        return await db.Hosts
            .Include(h => h.Group)
            .Where(h =>
                h.DisplayName.ToLower().Contains(term) ||
                h.Hostname.ToLower().Contains(term) ||
                h.Username.ToLower().Contains(term) ||
                (h.Notes != null && h.Notes.ToLower().Contains(term)))
            .OrderBy(h => h.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<HostEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts
            .Include(h => h.Group)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
    }

    public async Task AddAsync(HostEntry host, CancellationToken ct = default)
    {
        host.CreatedAt = DateTimeOffset.UtcNow;
        host.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Set sort order to be last in the group
        var maxOrder = await db.Hosts
            .Where(h => h.GroupId == host.GroupId)
            .MaxAsync(h => (int?)h.SortOrder, ct) ?? -1;
        host.SortOrder = maxOrder + 1;

        // Clear navigation property to prevent EF from trying to insert existing groups
        // The GroupId foreign key is sufficient for the relationship
        host.Group = null;

        db.Hosts.Add(host);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(HostEntry host, CancellationToken ct = default)
    {
        host.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation property to prevent EF from trying to insert existing groups
        host.Group = null;

        db.Hosts.Update(host);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.FindAsync([id], ct);
        if (host != null)
        {
            db.Hosts.Remove(host);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateSortOrderAsync(Guid id, int sortOrder, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.FindAsync([id], ct);
        if (host != null)
        {
            host.SortOrder = sortOrder;
            host.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ReorderHostsAsync(List<(Guid Id, int SortOrder)> hostOrders, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        foreach (var (id, sortOrder) in hostOrders)
        {
            var host = await db.Hosts.FindAsync([id], ct);
            if (host != null)
            {
                host.SortOrder = sortOrder;
                host.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
