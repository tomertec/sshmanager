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
        var history = await db.ConnectionHistory
            .Where(h => h.HostId == hostId)
            .ToListAsync(ct);

        if (history.Count == 0)
        {
            return new HostConnectionStats(null, 0, 0, 0);
        }

        var lastConnected = history.Max(h => h.ConnectedAt);
        var totalConnections = history.Count;
        var successfulConnections = history.Count(h => h.WasSuccessful);
        var successRate = totalConnections > 0 
            ? Math.Round((double)successfulConnections / totalConnections * 100, 1) 
            : 0;

        return new HostConnectionStats(lastConnected, totalConnections, successfulConnections, successRate);
    }

    public async Task<Dictionary<Guid, HostConnectionStats>> GetAllHostStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var history = await db.ConnectionHistory.ToListAsync(ct);

        var stats = history
            .GroupBy(h => h.HostId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var lastConnected = g.Max(h => h.ConnectedAt);
                    var totalConnections = g.Count();
                    var successfulConnections = g.Count(h => h.WasSuccessful);
                    var successRate = totalConnections > 0 
                        ? Math.Round((double)successfulConnections / totalConnections * 100, 1) 
                        : 0;
                    return new HostConnectionStats(lastConnected, totalConnections, successfulConnections, successRate);
                });

        return stats;
    }
}
