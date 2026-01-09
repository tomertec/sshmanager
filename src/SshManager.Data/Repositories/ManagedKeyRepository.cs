using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing tracked SSH keys.
/// </summary>
public sealed class ManagedKeyRepository : IManagedKeyRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ManagedKeyRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<ManagedSshKey>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ManagedSshKeys
            .OrderBy(k => k.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<ManagedSshKey?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ManagedSshKeys.FindAsync([id], ct);
    }

    public async Task<ManagedSshKey?> GetByPathAsync(string privateKeyPath, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ManagedSshKeys
            .FirstOrDefaultAsync(k => k.PrivateKeyPath == privateKeyPath, ct);
    }

    public async Task AddAsync(ManagedSshKey key, CancellationToken ct = default)
    {
        key.CreatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ManagedSshKeys.Add(key);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ManagedSshKey key, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ManagedSshKeys.Update(key);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var key = await db.ManagedSshKeys.FindAsync([id], ct);
        if (key != null)
        {
            db.ManagedSshKeys.Remove(key);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateLastUsedAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var key = await db.ManagedSshKeys.FindAsync([id], ct);
        if (key != null)
        {
            key.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> ExistsByPathAsync(string privateKeyPath, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ManagedSshKeys
            .AnyAsync(k => k.PrivateKeyPath == privateKeyPath, ct);
    }
}
