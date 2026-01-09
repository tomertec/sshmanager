using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing ProxyJump profiles.
/// </summary>
public sealed class ProxyJumpProfileRepository : IProxyJumpProfileRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ProxyJumpProfileRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<ProxyJumpProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ProxyJumpProfiles
            .Include(p => p.JumpHops.OrderBy(h => h.SortOrder))
                .ThenInclude(h => h.JumpHost)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<ProxyJumpProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ProxyJumpProfiles
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<ProxyJumpProfile?> GetByIdWithHopsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ProxyJumpProfiles
            .Include(p => p.JumpHops.OrderBy(h => h.SortOrder))
                .ThenInclude(h => h.JumpHost)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<ProxyJumpProfile> AddAsync(ProxyJumpProfile profile, CancellationToken ct = default)
    {
        profile.CreatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation properties to prevent EF from trying to insert existing entities
        foreach (var hop in profile.JumpHops)
        {
            hop.JumpHost = null;
            hop.Profile = null;
        }

        db.ProxyJumpProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task UpdateAsync(ProxyJumpProfile profile, CancellationToken ct = default)
    {
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Load existing profile with hops
        var existing = await db.ProxyJumpProfiles
            .Include(p => p.JumpHops)
            .FirstOrDefaultAsync(p => p.Id == profile.Id, ct);

        if (existing == null)
            return;

        // Update scalar properties
        existing.DisplayName = profile.DisplayName;
        existing.Description = profile.Description;
        existing.IsEnabled = profile.IsEnabled;
        existing.UpdatedAt = profile.UpdatedAt;

        // Remove hops that are no longer in the profile
        var incomingHopIds = profile.JumpHops.Select(h => h.Id).ToHashSet();
        var hopsToRemove = existing.JumpHops.Where(h => !incomingHopIds.Contains(h.Id)).ToList();
        foreach (var hop in hopsToRemove)
        {
            db.ProxyJumpHops.Remove(hop);
        }

        // Add or update hops
        foreach (var hop in profile.JumpHops)
        {
            var existingHop = existing.JumpHops.FirstOrDefault(h => h.Id == hop.Id);
            if (existingHop != null)
            {
                existingHop.JumpHostId = hop.JumpHostId;
                existingHop.SortOrder = hop.SortOrder;
            }
            else
            {
                hop.ProxyJumpProfileId = profile.Id;
                hop.Profile = null;
                hop.JumpHost = null;
                db.ProxyJumpHops.Add(hop);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var profile = await db.ProxyJumpProfiles.FindAsync([id], ct);
        if (profile != null)
        {
            db.ProxyJumpProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<ProxyJumpProfile>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync(ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var term = query.ToLowerInvariant();

        return await db.ProxyJumpProfiles
            .Include(p => p.JumpHops.OrderBy(h => h.SortOrder))
                .ThenInclude(h => h.JumpHost)
            .Where(p =>
                p.DisplayName.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)))
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task ReorderHopsAsync(Guid profileId, IEnumerable<Guid> hopIdsInOrder, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var profile = await db.ProxyJumpProfiles
            .Include(p => p.JumpHops)
            .FirstOrDefaultAsync(p => p.Id == profileId, ct);

        if (profile == null)
            return;

        var hopIdsList = hopIdsInOrder.ToList();
        for (int i = 0; i < hopIdsList.Count; i++)
        {
            var hop = profile.JumpHops.FirstOrDefault(h => h.Id == hopIdsList[i]);
            if (hop != null)
            {
                hop.SortOrder = i;
            }
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
