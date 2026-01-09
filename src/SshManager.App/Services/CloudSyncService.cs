using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;

namespace SshManager.App.Services;

/// <summary>
/// Implements encrypted cloud synchronization of host configurations.
/// </summary>
public class CloudSyncService : ICloudSyncService
{
    private const string SyncFileName = "sync.encrypted.json";
    private const string DeletedItemsFileName = "deleted-items.json";

    private readonly IHostRepository _hostRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IOneDrivePathDetector _oneDriveDetector;
    private readonly IPassphraseEncryptionService _encryptionService;
    private readonly ISyncConflictResolver _conflictResolver;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<CloudSyncService> _logger;

    private readonly object _statusLock = new();
    private SyncStatus _status = SyncStatus.Disabled;
    private List<SyncDeletedItem> _deletedItems = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CloudSyncService(
        IHostRepository hostRepo,
        IGroupRepository groupRepo,
        ISettingsRepository settingsRepo,
        IOneDrivePathDetector oneDriveDetector,
        IPassphraseEncryptionService encryptionService,
        ISyncConflictResolver conflictResolver,
        ISecretProtector secretProtector,
        ILogger<CloudSyncService>? logger = null)
    {
        _hostRepo = hostRepo;
        _groupRepo = groupRepo;
        _settingsRepo = settingsRepo;
        _oneDriveDetector = oneDriveDetector;
        _encryptionService = encryptionService;
        _conflictResolver = conflictResolver;
        _secretProtector = secretProtector;
        _logger = logger ?? NullLogger<CloudSyncService>.Instance;
    }

    /// <inheritdoc />
    public bool IsCloudSyncAvailable => _oneDriveDetector.IsOneDriveAvailable();

    /// <inheritdoc />
    public SyncStatus Status
    {
        get { lock (_statusLock) { return _status; } }
        private set
        {
            lock (_statusLock) { _status = value; }
        }
    }

    /// <inheritdoc />
    public async Task<bool> GetIsCloudSyncEnabledAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct).ConfigureAwait(false);
        return settings.EnableCloudSync;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastSyncTimeAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct).ConfigureAwait(false);
        return settings.LastSyncTime;
    }

    /// <inheritdoc />
    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
    public async Task SetupSyncAsync(string passphrase, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        _logger.LogInformation("Setting up cloud sync");
        SetStatus(SyncStatus.Syncing, "Setting up sync...");

        try
        {
            var settings = await _settingsRepo.GetAsync(ct);

            // Determine sync folder path
            var syncFolder = settings.SyncFolderPath;
            if (string.IsNullOrEmpty(syncFolder))
            {
                syncFolder = _oneDriveDetector.GetDefaultSyncFolderPath();
                if (string.IsNullOrEmpty(syncFolder))
                {
                    throw new InvalidOperationException("OneDrive is not available. Cannot set up cloud sync.");
                }
            }

            // Create sync folder if it doesn't exist
            Directory.CreateDirectory(syncFolder);

            // Generate device ID if not set
            if (string.IsNullOrEmpty(settings.SyncDeviceId))
            {
                settings.SyncDeviceId = Guid.NewGuid().ToString("N");
            }

            // Set device name if not set
            if (string.IsNullOrEmpty(settings.SyncDeviceName))
            {
                settings.SyncDeviceName = Environment.MachineName;
            }

            settings.SyncFolderPath = syncFolder;
            settings.EnableCloudSync = true;

            await _settingsRepo.UpdateAsync(settings, ct);

            // Load deleted items from local storage
            await LoadDeletedItemsAsync(ct);

            // If sync file exists, merge with it
            var syncFilePath = GetSyncFilePath(settings);
            if (File.Exists(syncFilePath))
            {
                _logger.LogInformation("Existing sync file found, performing initial sync");
                await SyncAsync(passphrase, ct);
            }
            else
            {
                // Create initial sync file
                _logger.LogInformation("Creating initial sync file");
                await WriteSyncFileAsync(passphrase, settings, ct);
            }

            SetStatus(SyncStatus.Idle, "Sync setup complete");
            _logger.LogInformation("Cloud sync setup complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set up cloud sync");
            SetStatus(SyncStatus.Error, $"Setup failed: {ex.Message}", ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SyncAsync(string passphrase, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        var settings = await _settingsRepo.GetAsync(ct);
        if (!settings.EnableCloudSync)
        {
            _logger.LogDebug("Cloud sync is disabled, skipping sync");
            return;
        }

        _logger.LogInformation("Starting sync operation");
        SetStatus(SyncStatus.Syncing, "Syncing...");

        try
        {
            var syncFilePath = GetSyncFilePath(settings);

            // Load local data
            var localData = await CreateLocalSyncDataAsync(settings, ct);

            // Load remote data if exists
            SyncData? remoteData = null;
            if (File.Exists(syncFilePath))
            {
                remoteData = await ReadSyncFileAsync(syncFilePath, passphrase, ct);
            }

            SyncData mergedData;
            if (remoteData != null)
            {
                // Merge local and remote data
                mergedData = _conflictResolver.Resolve(localData, remoteData);
            }
            else
            {
                mergedData = localData;
            }

            // Apply merged data to local database
            await ApplyMergedDataAsync(mergedData, passphrase, ct);

            // Write merged data back to sync file
            await WriteSyncFileAsync(passphrase, settings, ct);

            // Update last sync time
            settings.LastSyncTime = DateTimeOffset.UtcNow;
            await _settingsRepo.UpdateAsync(settings, ct);

            SetStatus(SyncStatus.Idle, "Sync complete");
            _logger.LogInformation("Sync completed successfully");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "Sync failed: Invalid passphrase");
            SetStatus(SyncStatus.Error, "Invalid passphrase", ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            SetStatus(SyncStatus.Error, $"Sync failed: {ex.Message}", ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisableSyncAsync(bool removeSyncFile = false, CancellationToken ct = default)
    {
        _logger.LogInformation("Disabling cloud sync (removeSyncFile: {RemoveFile})", removeSyncFile);

        var settings = await _settingsRepo.GetAsync(ct);

        if (removeSyncFile && !string.IsNullOrEmpty(settings.SyncFolderPath))
        {
            var syncFilePath = GetSyncFilePath(settings);
            if (File.Exists(syncFilePath))
            {
                File.Delete(syncFilePath);
                _logger.LogInformation("Deleted sync file: {Path}", syncFilePath);
            }
        }

        settings.EnableCloudSync = false;
        settings.LastSyncTime = null;
        await _settingsRepo.UpdateAsync(settings, ct);

        _deletedItems.Clear();
        await SaveDeletedItemsAsync(ct);

        SetStatus(SyncStatus.Disabled, "Sync disabled");
        _logger.LogInformation("Cloud sync disabled");
    }

    /// <inheritdoc />
    public async Task<bool> ValidatePassphraseAsync(string passphrase, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        var syncFilePath = GetSyncFilePath(settings);

        if (!File.Exists(syncFilePath))
        {
            return true; // No file to validate against
        }

        try
        {
            var json = await File.ReadAllTextAsync(syncFilePath, ct);
            var encryptedData = JsonSerializer.Deserialize<EncryptedSyncData>(json, JsonOptions);

            if (encryptedData == null)
            {
                return false;
            }

            return _encryptionService.VerifyPassphrase(encryptedData, passphrase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Passphrase validation failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SyncFileExistsAsync(CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(settings.SyncFolderPath))
        {
            return false;
        }

        var syncFilePath = GetSyncFilePath(settings);
        return File.Exists(syncFilePath);
    }

    /// <summary>
    /// Records a deleted item for sync propagation.
    /// Call this when a host or group is deleted locally.
    /// </summary>
    public async Task RecordDeletedItemAsync(Guid id, string itemType, CancellationToken ct = default)
    {
        _deletedItems.Add(new SyncDeletedItem
        {
            Id = id,
            ItemType = itemType,
            DeletedAt = DateTimeOffset.UtcNow
        });

        await SaveDeletedItemsAsync(ct);
        _logger.LogDebug("Recorded deleted item: {ItemType} {Id}", itemType, id);
    }

    private void SetStatus(SyncStatus status, string? message = null, Exception? exception = null)
    {
        Status = status;
        StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs
        {
            Status = status,
            Message = message,
            Exception = exception
        });
    }

    private static string GetSyncFilePath(AppSettings settings)
    {
        return Path.Combine(settings.SyncFolderPath ?? "", SyncFileName);
    }

    private string GetDeletedItemsFilePath()
    {
        var localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SshManager");
        return Path.Combine(localAppData, DeletedItemsFileName);
    }

    private async Task<SyncData> CreateLocalSyncDataAsync(AppSettings settings, CancellationToken ct)
    {
        var hosts = await _hostRepo.GetAllAsync(ct);
        var groups = await _groupRepo.GetAllAsync(ct);

        var syncData = new SyncData
        {
            Version = "1.0",
            DeviceId = settings.SyncDeviceId ?? Guid.NewGuid().ToString("N"),
            DeviceName = settings.SyncDeviceName ?? Environment.MachineName,
            ModifiedAt = DateTimeOffset.UtcNow,
            Hosts = hosts.Select(ConvertToSyncHost).ToList(),
            Groups = groups.Select(ConvertToSyncGroup).ToList(),
            DeletedItems = _deletedItems.ToList()
        };

        return syncData;
    }

    private SyncHostEntry ConvertToSyncHost(HostEntry host)
    {
        string? encryptedPassword = null;

        // For password auth, the password is already DPAPI-encrypted in storage.
        // For cloud sync, we store the DPAPI-encrypted value as-is.
        // Note: This means passwords won't sync across devices since DPAPI is machine-specific.
        // For cross-device password sync, users would need to re-enter passwords.
        if (host.AuthType == AuthType.Password && !string.IsNullOrEmpty(host.PasswordProtected))
        {
            // Store as marker that password exists but needs to be re-entered on other devices
            encryptedPassword = "[DPAPI-PROTECTED]";
        }

        return new SyncHostEntry
        {
            Id = host.Id,
            DisplayName = host.DisplayName,
            Hostname = host.Hostname,
            Port = host.Port,
            Username = host.Username,
            AuthType = host.AuthType,
            PrivateKeyPath = host.PrivateKeyPath,
            EncryptedPassword = encryptedPassword,
            Notes = host.Notes,
            GroupId = host.GroupId,
            CreatedAt = host.CreatedAt,
            UpdatedAt = host.UpdatedAt
        };
    }

    private static SyncHostGroup ConvertToSyncGroup(HostGroup group)
    {
        return new SyncHostGroup
        {
            Id = group.Id,
            Name = group.Name,
            Icon = group.Icon,
            SortOrder = group.SortOrder,
            StatusCheckIntervalSeconds = group.StatusCheckIntervalSeconds,
            CreatedAt = group.CreatedAt
        };
    }

    private HostEntry ConvertFromSyncHost(SyncHostEntry syncHost)
    {
        return new HostEntry
        {
            Id = syncHost.Id,
            DisplayName = syncHost.DisplayName,
            Hostname = syncHost.Hostname,
            Port = syncHost.Port,
            Username = syncHost.Username,
            AuthType = syncHost.AuthType,
            PrivateKeyPath = syncHost.PrivateKeyPath,
            PasswordProtected = null, // Password needs to be re-entered on new device
            Notes = syncHost.Notes,
            GroupId = syncHost.GroupId,
            CreatedAt = syncHost.CreatedAt,
            UpdatedAt = syncHost.UpdatedAt
        };
    }

    private static HostGroup ConvertFromSyncGroup(SyncHostGroup syncGroup)
    {
        return new HostGroup
        {
            Id = syncGroup.Id,
            Name = syncGroup.Name,
            Icon = syncGroup.Icon,
            SortOrder = syncGroup.SortOrder,
            StatusCheckIntervalSeconds = syncGroup.StatusCheckIntervalSeconds,
            CreatedAt = syncGroup.CreatedAt
        };
    }

    private async Task<SyncData?> ReadSyncFileAsync(string filePath, string passphrase, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var encryptedData = JsonSerializer.Deserialize<EncryptedSyncData>(json, JsonOptions);

        if (encryptedData == null)
        {
            return null;
        }

        var decryptedJson = _encryptionService.Decrypt(encryptedData, passphrase);
        return JsonSerializer.Deserialize<SyncData>(decryptedJson, JsonOptions);
    }

    private async Task WriteSyncFileAsync(string passphrase, AppSettings settings, CancellationToken ct)
    {
        var syncData = await CreateLocalSyncDataAsync(settings, ct);
        var syncJson = JsonSerializer.Serialize(syncData, JsonOptions);

        var encryptedData = _encryptionService.Encrypt(syncJson, passphrase);
        encryptedData.DeviceId = settings.SyncDeviceId ?? "";
        encryptedData.DeviceName = settings.SyncDeviceName ?? "";

        var encryptedJson = JsonSerializer.Serialize(encryptedData, JsonOptions);

        var syncFilePath = GetSyncFilePath(settings);
        await File.WriteAllTextAsync(syncFilePath, encryptedJson, ct);

        _logger.LogDebug("Wrote sync file: {Path}", syncFilePath);
    }

    private async Task ApplyMergedDataAsync(SyncData mergedData, string passphrase, CancellationToken ct)
    {
        var existingHosts = await _hostRepo.GetAllAsync(ct);
        var existingGroups = await _groupRepo.GetAllAsync(ct);

        var existingHostIds = existingHosts.Select(h => h.Id).ToHashSet();
        var existingGroupIds = existingGroups.Select(g => g.Id).ToHashSet();

        // Apply groups first (hosts may reference them)
        foreach (var syncGroup in mergedData.Groups)
        {
            var group = ConvertFromSyncGroup(syncGroup);

            if (existingGroupIds.Contains(group.Id))
            {
                await _groupRepo.UpdateAsync(group, ct);
            }
            else
            {
                await _groupRepo.AddAsync(group, ct);
            }
        }

        // Delete groups that are not in merged data
        var mergedGroupIds = mergedData.Groups.Select(g => g.Id).ToHashSet();
        foreach (var existingGroup in existingGroups)
        {
            if (!mergedGroupIds.Contains(existingGroup.Id))
            {
                await _groupRepo.DeleteAsync(existingGroup.Id, ct);
            }
        }

        // Apply hosts
        foreach (var syncHost in mergedData.Hosts)
        {
            var host = ConvertFromSyncHost(syncHost);

            // Preserve local password if it exists
            var existingHost = existingHosts.FirstOrDefault(h => h.Id == host.Id);
            if (existingHost != null && !string.IsNullOrEmpty(existingHost.PasswordProtected))
            {
                host.PasswordProtected = existingHost.PasswordProtected;
            }

            if (existingHostIds.Contains(host.Id))
            {
                await _hostRepo.UpdateAsync(host, ct);
            }
            else
            {
                await _hostRepo.AddAsync(host, ct);
            }
        }

        // Delete hosts that are not in merged data
        var mergedHostIds = mergedData.Hosts.Select(h => h.Id).ToHashSet();
        foreach (var existingHost in existingHosts)
        {
            if (!mergedHostIds.Contains(existingHost.Id))
            {
                await _hostRepo.DeleteAsync(existingHost.Id, ct);
            }
        }

        // Update deleted items list
        _deletedItems = mergedData.DeletedItems.ToList();
        await SaveDeletedItemsAsync(ct);
    }

    private async Task LoadDeletedItemsAsync(CancellationToken ct)
    {
        var filePath = GetDeletedItemsFilePath();
        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath, ct);
                _deletedItems = JsonSerializer.Deserialize<List<SyncDeletedItem>>(json, JsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load deleted items file");
                _deletedItems = [];
            }
        }
    }

    private async Task SaveDeletedItemsAsync(CancellationToken ct)
    {
        var filePath = GetDeletedItemsFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_deletedItems, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }
}
