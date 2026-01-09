using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing connection history.
/// </summary>
public sealed class ConnectionHistoryRepository : IConnectionHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ConnectionHistoryRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<ConnectionHistory>> GetRecentAsync(int count = 20, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entries = await db.ConnectionHistory
            .Include(h => h.Host)
            .ToListAsync(ct);

        // Order client-side because SQLite doesn't support DateTimeOffset in ORDER BY
        return entries
            .OrderByDescending(h => h.ConnectedAt)
            .Take(count)
            .ToList();
    }

    public async Task<List<ConnectionHistory>> GetByHostAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entries = await db.ConnectionHistory
            .Where(h => h.HostId == hostId)
            .ToListAsync(ct);

        // Order client-side because SQLite doesn't support DateTimeOffset in ORDER BY
        return entries
            .OrderByDescending(h => h.ConnectedAt)
            .ToList();
    }

    public async Task AddAsync(ConnectionHistory entry, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ConnectionHistory.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ConnectionHistory entry, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ConnectionHistory.Update(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.ConnectionHistory.ExecuteDeleteAsync(ct);
    }

    public async Task ClearOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.ConnectionHistory
            .Where(h => h.ConnectedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
