using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing command history.
/// </summary>
public sealed class CommandHistoryRepository : ICommandHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CommandHistoryRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<CommandHistoryEntry>> GetSuggestionsAsync(
        Guid? hostId,
        string prefix,
        int maxResults,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.CommandHistory.AsQueryable();

        // Filter by host if specified
        if (hostId.HasValue)
        {
            query = query.Where(c => c.HostId == hostId.Value);
        }

        // Filter by prefix (case-insensitive)
        query = query.Where(c => EF.Functions.Like(c.Command, $"{prefix}%"));

        // Order by use count (descending) and last execution (descending)
        var results = await query
            .OrderByDescending(c => c.UseCount)
            .ThenByDescending(c => c.ExecutedAt)
            .Take(maxResults)
            .ToListAsync(ct);

        return results;
    }

    public async Task AddAsync(Guid? hostId, string command, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Check if command already exists for this host
        var existing = await db.CommandHistory
            .FirstOrDefaultAsync(c => c.HostId == hostId && c.Command == command, ct);

        if (existing != null)
        {
            // Update existing entry
            existing.UseCount++;
            existing.ExecutedAt = DateTimeOffset.UtcNow;
            db.CommandHistory.Update(existing);
        }
        else
        {
            // Insert new entry
            var newEntry = new CommandHistoryEntry
            {
                Id = Guid.NewGuid(),
                HostId = hostId,
                Command = command,
                ExecutedAt = DateTimeOffset.UtcNow,
                UseCount = 1
            };
            db.CommandHistory.Add(newEntry);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<CommandHistoryEntry>> GetRecentAsync(
        Guid? hostId,
        int count,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.CommandHistory.AsQueryable();

        // Filter by host if specified
        if (hostId.HasValue)
        {
            query = query.Where(c => c.HostId == hostId.Value);
        }

        var results = await query
            .OrderByDescending(c => c.ExecutedAt)
            .Take(count)
            .ToListAsync(ct);

        return results;
    }

    public async Task ClearHostHistoryAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await db.CommandHistory
            .Where(c => c.HostId == hostId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await db.CommandHistory.ExecuteDeleteAsync(ct);
    }

    public async Task<List<CommandHistoryEntry>> GetMostUsedAsync(
        Guid? hostId,
        int count,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.CommandHistory.AsQueryable();

        // Filter by host if specified
        if (hostId.HasValue)
        {
            query = query.Where(c => c.HostId == hostId.Value);
        }

        var results = await query
            .OrderByDescending(c => c.UseCount)
            .ThenByDescending(c => c.ExecutedAt)
            .Take(count)
            .ToListAsync(ct);

        return results;
    }
}
