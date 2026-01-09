using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

public sealed class SessionRecordingRepository : ISessionRecordingRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SessionRecordingRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<SessionRecording>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recordings = await db.SessionRecordings
            .Include(x => x.Host)
            .ToListAsync(ct);
        return recordings.OrderByDescending(x => x.StartedAt).ToList();
    }

    public async Task<List<SessionRecording>> GetByHostAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recordings = await db.SessionRecordings
            .Include(x => x.Host)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        return recordings.OrderByDescending(x => x.StartedAt).ToList();
    }

    public async Task<SessionRecording?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SessionRecordings
            .Include(x => x.Host)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task AddAsync(SessionRecording recording, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        recording.CreatedAt = DateTimeOffset.UtcNow;
        recording.UpdatedAt = DateTimeOffset.UtcNow;
        db.SessionRecordings.Add(recording);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SessionRecording recording, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        recording.UpdatedAt = DateTimeOffset.UtcNow;
        db.SessionRecordings.Update(recording);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateDurationAndSizeAsync(Guid id, TimeSpan duration, long fileSizeBytes, long eventCount, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recording = await db.SessionRecordings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (recording is null)
            return;

        recording.Duration = duration;
        recording.FileSizeBytes = fileSizeBytes;
        recording.EventCount = eventCount;
        recording.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recording = await db.SessionRecordings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (recording is null)
            return;

        db.SessionRecordings.Remove(recording);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<SessionRecording>> GetOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recordings = await db.SessionRecordings
            .ToListAsync(ct);
        return recordings
            .Where(x => x.StartedAt < cutoff)
            .OrderBy(x => x.StartedAt)
            .ToList();
    }

    public async Task<long> GetTotalStorageSizeAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SessionRecordings.SumAsync(x => (long?)x.FileSizeBytes, ct) ?? 0L;
    }
}
