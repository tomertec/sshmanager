namespace SshManager.Core.Models;

/// <summary>
/// Container for all synchronized data.
/// This is what gets encrypted and stored in the cloud sync file.
/// </summary>
public class SyncData
{
    /// <summary>
    /// Schema version for future compatibility.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Unique identifier of the device that last modified this data.
    /// </summary>
    public string DeviceId { get; set; } = "";

    /// <summary>
    /// Human-readable name of the device that last modified this data.
    /// </summary>
    public string DeviceName { get; set; } = "";

    /// <summary>
    /// Timestamp when the data was last modified.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>
    /// All host entries to synchronize.
    /// </summary>
    public List<SyncHostEntry> Hosts { get; set; } = [];

    /// <summary>
    /// All host groups to synchronize.
    /// </summary>
    public List<SyncHostGroup> Groups { get; set; } = [];

    /// <summary>
    /// Items deleted within the retention window (for tombstone-based sync).
    /// </summary>
    public List<SyncDeletedItem> DeletedItems { get; set; } = [];
}

/// <summary>
/// Host entry data for synchronization.
/// Mirrors HostEntry but without navigation properties and with re-encrypted password.
/// </summary>
public class SyncHostEntry
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Hostname { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public AuthType AuthType { get; set; }
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Password encrypted with the sync passphrase (not DPAPI).
    /// Empty for non-password auth types.
    /// </summary>
    public string? EncryptedPassword { get; set; }

    public string? Notes { get; set; }
    public Guid? GroupId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Host group data for synchronization.
/// </summary>
public class SyncHostGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public int StatusCheckIntervalSeconds { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Represents a deleted item for tombstone-based synchronization.
/// Deleted items are kept for a retention period to propagate deletions across devices.
/// </summary>
public class SyncDeletedItem
{
    /// <summary>
    /// The ID of the deleted item.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Type of the deleted item ("Host" or "Group").
    /// </summary>
    public string ItemType { get; set; } = "";

    /// <summary>
    /// When the item was deleted.
    /// </summary>
    public DateTimeOffset DeletedAt { get; set; }
}
