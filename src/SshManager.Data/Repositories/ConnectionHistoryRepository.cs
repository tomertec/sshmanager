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
        return await db.ConnectionHistory
            .Include(h => h.Host)
            .OrderByDescending(h => h.ConnectedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<List<ConnectionHistory>> GetByHostAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ConnectionHistory
            .Include(h => h.Host)
            .Where(h => h.HostId == hostId)
            .OrderByDescending(h => h.ConnectedAt)
            .ToListAsync(ct);
    }

    public async Task<List<HostEntry>> GetRecentUniqueHostsAsync(int count = 5, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Get the most recent successful connection per host (in SQL)
        var recentIds = await db.ConnectionHistory
            .Where(h => h.WasSuccessful && h.Host != null)
            .GroupBy(h => h.HostId)
            .Select(g => new { HostId = g.Key, LastConnected = g.Max(h => h.ConnectedAt) })
            .OrderByDescending(x => x.LastConnected)
            .Take(count)
            .Select(x => x.HostId)
            .ToListAsync(ct);

        // Fetch the hosts with their groups
        return await db.ConnectionHistory
            .Include(h => h.Host)
                .ThenInclude(h => h!.Group)
            .Where(h => recentIds.Contains(h.HostId))
            .GroupBy(h => h.HostId)
            .Select(g => g.OrderByDescending(h => h.ConnectedAt).First().Host!)
            .ToListAsync(ct);
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

    public async Task<int> CountOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ConnectionHistory
            .Where(h => h.ConnectedAt < cutoff)
            .CountAsync(ct);
    }

    public async Task<HostConnectionStats> GetHostStatsAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Aggregate in SQL
        var stats = await db.ConnectionHistory
            .Where(h => h.HostId == hostId)
            .GroupBy(h => 1)
            .Select(g => new
            {
                LastConnected = g.Max(h => (DateTimeOffset?)h.ConnectedAt),
                TotalConnections = g.Count(),
                SuccessfulConnections = g.Count(h => h.WasSuccessful)
            })
            .FirstOrDefaultAsync(ct);

        if (stats == null)
        {
            return new HostConnectionStats(null, 0, 0, 0);
        }

        var successRate = stats.TotalConnections > 0
            ? Math.Round((double)stats.SuccessfulConnections / stats.TotalConnections * 100, 1)
            : 0;

        return new HostConnectionStats(stats.LastConnected, stats.TotalConnections, stats.SuccessfulConnections, successRate);
    }

    public async Task<Dictionary<Guid, HostConnectionStats>> GetAllHostStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Aggregate in SQL
        var stats = await db.ConnectionHistory
            .GroupBy(h => h.HostId)
            .Select(g => new
            {
                HostId = g.Key,
                LastConnected = g.Max(h => (DateTimeOffset?)h.ConnectedAt),
                TotalConnections = g.Count(),
                SuccessfulConnections = g.Count(h => h.WasSuccessful)
            })
            .ToListAsync(ct);

        return stats.ToDictionary(
            s => s.HostId,
            s =>
            {
                var successRate = s.TotalConnections > 0
                    ? Math.Round((double)s.SuccessfulConnections / s.TotalConnections * 100, 1)
                    : 0;
                return new HostConnectionStats(s.LastConnected, s.TotalConnections, s.SuccessfulConnections, successRate);
            });
    }
}
