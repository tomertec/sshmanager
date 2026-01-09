using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing host groups.
/// </summary>
public sealed class GroupRepository : IGroupRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public GroupRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<HostGroup>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Groups
            .Include(g => g.Hosts)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name)
            .ToListAsync(ct);
    }

    public async Task<HostGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Groups
            .Include(g => g.Hosts)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task AddAsync(HostGroup group, CancellationToken ct = default)
    {
        group.CreatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Set sort order to be last
        var maxOrder = await db.Groups.MaxAsync(g => (int?)g.SortOrder, ct) ?? -1;
        group.SortOrder = maxOrder + 1;

        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(HostGroup group, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Groups.Update(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var group = await db.Groups.FindAsync([id], ct);
        if (group != null)
        {
            // Set hosts in this group to ungrouped
            var hosts = await db.Hosts.Where(h => h.GroupId == id).ToListAsync(ct);
            foreach (var host in hosts)
            {
                host.GroupId = null;
            }

            db.Groups.Remove(group);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ReorderAsync(List<Guid> orderedIds, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var groups = await db.Groups.ToDictionaryAsync(g => g.Id, ct);

        for (int i = 0; i < orderedIds.Count; i++)
        {
            if (groups.TryGetValue(orderedIds[i], out var group))
            {
                group.SortOrder = i;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
