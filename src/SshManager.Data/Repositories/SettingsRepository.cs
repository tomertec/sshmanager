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

        // Ensure we have the entity attached
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Id == settings.Id, ct);
        if (existing != null)
        {
            // Terminal settings
            existing.UseEmbeddedTerminal = settings.UseEmbeddedTerminal;
            existing.TerminalFontFamily = settings.TerminalFontFamily;
            existing.TerminalFontSize = settings.TerminalFontSize;
            existing.ScrollbackBufferSize = settings.ScrollbackBufferSize;
            existing.EnableFindInTerminal = settings.EnableFindInTerminal;
            existing.FindCaseSensitiveDefault = settings.FindCaseSensitiveDefault;
            existing.TerminalThemeId = settings.TerminalThemeId;

            // Connection settings
            existing.DefaultPort = settings.DefaultPort;
            existing.ConnectionTimeoutSeconds = settings.ConnectionTimeoutSeconds;
            existing.KeepAliveIntervalSeconds = settings.KeepAliveIntervalSeconds;
            existing.AutoReconnect = settings.AutoReconnect;
            existing.MaxReconnectAttempts = settings.MaxReconnectAttempts;

            // Security settings
            existing.DefaultKeyPath = settings.DefaultKeyPath;
            existing.PreferredAuthMethod = settings.PreferredAuthMethod;

            // Credential caching settings
            existing.EnableCredentialCaching = settings.EnableCredentialCaching;
            existing.CredentialCacheTimeoutMinutes = settings.CredentialCacheTimeoutMinutes;
            existing.ClearCacheOnLock = settings.ClearCacheOnLock;
            existing.ClearCacheOnExit = settings.ClearCacheOnExit;

            // Application behavior
            existing.ConfirmOnClose = settings.ConfirmOnClose;
            existing.RememberWindowPosition = settings.RememberWindowPosition;
            existing.Theme = settings.Theme;
            existing.StartMinimized = settings.StartMinimized;
            existing.MinimizeToTray = settings.MinimizeToTray;

            // Session logging settings
            existing.EnableSessionLogging = settings.EnableSessionLogging;
            existing.SessionLogDirectory = settings.SessionLogDirectory;
            existing.SessionLogTimestampLines = settings.SessionLogTimestampLines;
            existing.MaxLogFileSizeMB = settings.MaxLogFileSizeMB;
            existing.MaxLogFilesToKeep = settings.MaxLogFilesToKeep;
            existing.SessionLogLevel = settings.SessionLogLevel;
            existing.RedactTypedSecrets = settings.RedactTypedSecrets;

            // History settings
            existing.MaxHistoryEntries = settings.MaxHistoryEntries;
            existing.HistoryRetentionDays = settings.HistoryRetentionDays;

            // Window position
            existing.WindowX = settings.WindowX;
            existing.WindowY = settings.WindowY;
            existing.WindowWidth = settings.WindowWidth;
            existing.WindowHeight = settings.WindowHeight;

            // Backup settings
            existing.EnableAutoBackup = settings.EnableAutoBackup;
            existing.BackupIntervalMinutes = settings.BackupIntervalMinutes;
            existing.MaxBackupCount = settings.MaxBackupCount;
            existing.BackupDirectory = settings.BackupDirectory;
            existing.LastAutoBackupTime = settings.LastAutoBackupTime;

            // Cloud sync settings
            existing.EnableCloudSync = settings.EnableCloudSync;
            existing.SyncFolderPath = settings.SyncFolderPath;
            existing.SyncDeviceId = settings.SyncDeviceId;
            existing.SyncDeviceName = settings.SyncDeviceName;
            existing.LastSyncTime = settings.LastSyncTime;
            existing.SyncIntervalMinutes = settings.SyncIntervalMinutes;

            // Split pane settings
            existing.EnableSplitPanes = settings.EnableSplitPanes;
            existing.ShowPaneHeaders = settings.ShowPaneHeaders;
            existing.DefaultSplitOrientation = settings.DefaultSplitOrientation;
            existing.MinimumPaneSize = settings.MinimumPaneSize;

            await db.SaveChangesAsync(ct);
        }
        else
        {
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
    }
}
