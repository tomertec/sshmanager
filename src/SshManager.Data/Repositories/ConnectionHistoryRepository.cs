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
        var history = await db.ConnectionHistory
            .Include(h => h.Host)
            .ToListAsync(ct);
        return history
            .OrderByDescending(h => h.ConnectedAt)
            .Take(count)
            .ToList();
    }

    public async Task<List<ConnectionHistory>> GetByHostAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var history = await db.ConnectionHistory
            .Include(h => h.Host)
            .Where(h => h.HostId == hostId)
            .ToListAsync(ct);
        return history.OrderByDescending(h => h.ConnectedAt).ToList();
    }

    public async Task<List<HostEntry>> GetRecentUniqueHostsAsync(int count = 5, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recentHosts = await db.ConnectionHistory
            .Include(h => h.Host)
                .ThenInclude(h => h!.Group)
            .Where(h => h.Host != null && h.WasSuccessful)
            .OrderByDescending(h => h.ConnectedAt)
            .ToListAsync(ct);

        // Get unique hosts in order of most recent connection
        var uniqueHosts = recentHosts
            .GroupBy(h => h.HostId)
            .Select(g => g.First().Host!)
            .Take(count)
            .ToList();

        return uniqueHosts;
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
