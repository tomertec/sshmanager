using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing port forwarding profiles.
/// </summary>
public sealed class PortForwardingProfileRepository : IPortForwardingProfileRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PortForwardingProfileRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<PortForwardingProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PortForwardingProfiles
            .Include(p => p.Host)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PortForwardingProfile>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PortForwardingProfiles
            .Include(p => p.Host)
            .Where(p => p.HostId == hostId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PortForwardingProfile>> GetGlobalProfilesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PortForwardingProfiles
            .Where(p => p.HostId == null)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<PortForwardingProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PortForwardingProfiles
            .Include(p => p.Host)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PortForwardingProfile> AddAsync(PortForwardingProfile profile, CancellationToken ct = default)
    {
        profile.CreatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation property to prevent EF from trying to insert existing entities
        profile.Host = null;

        db.PortForwardingProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task UpdateAsync(PortForwardingProfile profile, CancellationToken ct = default)
    {
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation property to prevent EF from trying to insert existing entities
        profile.Host = null;

        db.PortForwardingProfiles.Update(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var profile = await db.PortForwardingProfiles.FindAsync([id], ct);
        if (profile != null)
        {
            db.PortForwardingProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> IsPortInUseAsync(int localPort, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.PortForwardingProfiles
            .Where(p => p.LocalPort == localPort && p.IsEnabled);

        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }
}
