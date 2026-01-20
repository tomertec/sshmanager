using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for saved sessions using EF Core.
/// </summary>
public sealed class SavedSessionRepository : ISavedSessionRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SavedSessionRepository> _logger;

    public SavedSessionRepository(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<SavedSessionRepository>? logger = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _logger = logger ?? NullLogger<SavedSessionRepository>.Instance;
    }

    public async Task<List<SavedSession>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SavedSessions.ToListAsync(ct);
    }

    public async Task<List<SavedSession>> GetRecoverableSessionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SavedSessions
            .Where(s => !s.WasGracefulShutdown)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(SavedSession session, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.SavedSessions.FindAsync([session.Id], ct);
        if (existing != null)
        {
            existing.Title = session.Title;
            existing.SavedAt = DateTimeOffset.UtcNow;
            existing.WasGracefulShutdown = session.WasGracefulShutdown;
        }
        else
        {
            session.SavedAt = DateTimeOffset.UtcNow;
            db.SavedSessions.Add(session);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Saved session {SessionId} for host {HostId}", session.Id, session.HostEntryId);
    }

    public async Task SaveAllAsync(IEnumerable<SavedSession> sessions, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        foreach (var session in sessions)
        {
            var existing = await db.SavedSessions.FindAsync([session.Id], ct);
            if (existing != null)
            {
                existing.Title = session.Title;
                existing.SavedAt = DateTimeOffset.UtcNow;
                existing.WasGracefulShutdown = session.WasGracefulShutdown;
            }
            else
            {
                session.SavedAt = DateTimeOffset.UtcNow;
                db.SavedSessions.Add(session);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Saved {Count} sessions", sessions.Count());
    }

    public async Task MarkAsGracefulAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.SavedSessions.FindAsync([id], ct);
        if (session != null)
        {
            session.WasGracefulShutdown = true;
            session.SavedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("Marked session {SessionId} as graceful shutdown", id);
        }
    }

    public async Task MarkAllAsGracefulAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sessions = await db.SavedSessions.ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.WasGracefulShutdown = true;
            session.SavedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Marked {Count} sessions as graceful shutdown", sessions.Count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.SavedSessions.FindAsync([id], ct);
        if (session != null)
        {
            db.SavedSessions.Remove(session);
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("Deleted saved session {SessionId}", id);
        }
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sessions = await db.SavedSessions.ToListAsync(ct);
        db.SavedSessions.RemoveRange(sessions);
        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Cleared all {Count} saved sessions", sessions.Count);
    }
}
