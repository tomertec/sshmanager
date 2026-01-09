using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SshManager.Data;
using SshManager.Data.Repositories;

namespace SshManager.App.Services;

/// <summary>
/// Service for creating, restoring, and managing backup files.
/// </summary>
public class BackupService : IBackupService
{
    private readonly IExportImportService _exportService;
    private readonly IHostRepository _hostRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<BackupService> _logger;
    private readonly string _defaultBackupDir;

    public BackupService(
        IExportImportService exportService,
        IHostRepository hostRepo,
        IGroupRepository groupRepo,
        ISettingsRepository settingsRepo,
        ILogger<BackupService> logger)
    {
        _exportService = exportService;
        _hostRepo = hostRepo;
        _groupRepo = groupRepo;
        _settingsRepo = settingsRepo;
        _logger = logger;

        _defaultBackupDir = Path.Combine(DbPaths.GetAppDataDir(), "backups");
    }

    public async Task<string> GetBackupDirectoryAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct).ConfigureAwait(false);
        var dir = !string.IsNullOrEmpty(settings.BackupDirectory)
            ? settings.BackupDirectory
            : _defaultBackupDir;

        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task OpenBackupDirectoryAsync(CancellationToken ct = default)
    {
        var dir = await GetBackupDirectoryAsync(ct).ConfigureAwait(false);
        Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", dir);
    }

    public async Task<BackupInfo> CreateBackupAsync(CancellationToken ct = default)
    {
        var backupDir = await GetBackupDirectoryAsync(ct).ConfigureAwait(false);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        var fileName = $"backup-{timestamp}.json";
        var filePath = Path.Combine(backupDir, fileName);

        _logger.LogInformation("Creating backup: {FilePath}", filePath);

        var hosts = await _hostRepo.GetAllAsync(ct);
        var groups = await _groupRepo.GetAllAsync(ct);

        await _exportService.ExportAsync(filePath, hosts, groups, ct);

        var fileInfo = new FileInfo(filePath);
        var backupInfo = new BackupInfo
        {
            FilePath = filePath,
            FileName = fileName,
            CreatedAt = DateTimeOffset.Now,
            FileSizeBytes = fileInfo.Length,
            HostCount = hosts.Count,
            GroupCount = groups.Count
        };

        _logger.LogInformation("Backup created: {FileName} ({HostCount} hosts, {GroupCount} groups)",
            fileName, hosts.Count, groups.Count);

        return backupInfo;
    }

    public async Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken ct = default)
    {
        var backupDir = await GetBackupDirectoryAsync(ct).ConfigureAwait(false);
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(backupDir))
            return backups;

        var files = Directory.GetFiles(backupDir, "backup-*.json")
            .OrderByDescending(f => f);

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            var backup = new BackupInfo
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                CreatedAt = fileInfo.CreationTime,
                FileSizeBytes = fileInfo.Length
            };

            // Try to read host/group counts from the file
            try
            {
                var counts = await GetBackupCountsAsync(filePath, ct);
                backup.HostCount = counts.HostCount;
                backup.GroupCount = counts.GroupCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read backup counts from {FileName}", fileInfo.Name);
            }

            backups.Add(backup);
        }

        return backups;
    }

    public async Task<(int HostCount, int GroupCount)> RestoreBackupAsync(string backupPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Restoring backup from: {FilePath}", backupPath);

        var (hosts, groups) = await _exportService.ImportAsync(backupPath, ct);

        // Add groups first (hosts may reference them)
        foreach (var group in groups)
        {
            await _groupRepo.AddAsync(group, ct);
        }

        // Then add hosts
        foreach (var host in hosts)
        {
            await _hostRepo.AddAsync(host, ct);
        }

        _logger.LogInformation("Restored {HostCount} hosts and {GroupCount} groups",
            hosts.Count, groups.Count);

        return (hosts.Count, groups.Count);
    }

    public async Task<int> CleanupOldBackupsAsync(int keepCount, CancellationToken ct = default)
    {
        var backups = await GetBackupsAsync(ct);
        var toDelete = backups.Skip(keepCount).ToList();
        var deleted = 0;

        foreach (var backup in toDelete)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                File.Delete(backup.FilePath);
                deleted++;
                _logger.LogDebug("Deleted old backup: {FileName}", backup.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete backup: {FileName}", backup.FileName);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old backups", deleted);
        }

        return deleted;
    }

    public Task DeleteBackupAsync(string backupPath, CancellationToken ct = default)
    {
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
            _logger.LogInformation("Deleted backup: {FilePath}", backupPath);
        }
        return Task.CompletedTask;
    }

    private async Task<(int HostCount, int GroupCount)> GetBackupCountsAsync(string filePath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hostCount = 0;
        var groupCount = 0;

        if (root.TryGetProperty("hosts", out var hostsElement))
        {
            hostCount = hostsElement.GetArrayLength();
        }
        if (root.TryGetProperty("groups", out var groupsElement))
        {
            groupCount = groupsElement.GetArrayLength();
        }

        return (hostCount, groupCount);
    }
}
