using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository for managing SSH host fingerprints.
/// </summary>
public class HostFingerprintRepository : IHostFingerprintRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<HostFingerprintRepository> _logger;

    public HostFingerprintRepository(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<HostFingerprintRepository>? logger = null)
    {
        _dbFactory = dbFactory;
        _logger = logger ?? NullLogger<HostFingerprintRepository>.Instance;
    }

    public async Task<HostFingerprint?> GetByHostAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HostFingerprints
            .FirstOrDefaultAsync(f => f.HostId == hostId, ct);
    }

    public async Task<IReadOnlyList<HostFingerprint>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HostFingerprints
            .Include(f => f.Host)
            .OrderBy(f => f.Host!.DisplayName)
            .ToListAsync(ct);
    }

    public async Task AddAsync(HostFingerprint fingerprint, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.HostFingerprints.Add(fingerprint);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Added fingerprint for host {HostId}: {Algorithm}", fingerprint.HostId, fingerprint.Algorithm);
    }

    public async Task UpdateLastSeenAsync(Guid fingerprintId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fingerprint = await db.HostFingerprints.FindAsync([fingerprintId], ct);
        if (fingerprint != null)
        {
            fingerprint.LastSeen = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateAsync(HostFingerprint fingerprint, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.HostFingerprints.Update(fingerprint);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated fingerprint for host {HostId}", fingerprint.HostId);
    }

    public async Task DeleteAsync(Guid fingerprintId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fingerprint = await db.HostFingerprints.FindAsync([fingerprintId], ct);
        if (fingerprint != null)
        {
            db.HostFingerprints.Remove(fingerprint);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted fingerprint {FingerprintId}", fingerprintId);
        }
    }

    public async Task DeleteByHostAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fingerprints = await db.HostFingerprints
            .Where(f => f.HostId == hostId)
            .ToListAsync(ct);

        if (fingerprints.Count > 0)
        {
            db.HostFingerprints.RemoveRange(fingerprints);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted {Count} fingerprints for host {HostId}", fingerprints.Count, hostId);
        }
    }
}
