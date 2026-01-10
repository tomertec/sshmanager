using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing host profiles.
/// </summary>
public sealed class HostProfileRepository : IHostProfileRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HostProfileRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<HostProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HostProfiles
            .Include(p => p.ProxyJumpProfile)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<HostProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HostProfiles
            .Include(p => p.ProxyJumpProfile)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task AddAsync(HostProfile profile, CancellationToken ct = default)
    {
        profile.CreatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation property to prevent EF from trying to insert existing ProxyJumpProfile
        profile.ProxyJumpProfile = null;

        db.HostProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(HostProfile profile, CancellationToken ct = default)
    {
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation property to prevent EF from trying to insert existing ProxyJumpProfile
        profile.ProxyJumpProfile = null;

        db.HostProfiles.Update(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var profile = await db.HostProfiles.FindAsync([id], ct);
        if (profile != null)
        {
            db.HostProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }
}
