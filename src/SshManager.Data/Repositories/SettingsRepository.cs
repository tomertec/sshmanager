using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for application settings.
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SettingsRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Settings.FirstOrDefaultAsync(ct);

        if (settings == null)
        {
            // Create default settings
            settings = new AppSettings();
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        return settings;
    }

    public async Task UpdateAsync(AppSettings settings, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Id == settings.Id, ct);
        if (existing != null)
        {
            // Use EF Core's SetValues to automatically copy all scalar properties
            // This eliminates manual property copying and automatically handles new properties
            db.Entry(existing).CurrentValues.SetValues(settings);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
    }
}
