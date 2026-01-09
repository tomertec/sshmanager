using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Resolves sync conflicts using a last-modified-wins strategy with tombstone-based deletions.
/// </summary>
public class SyncConflictResolver : ISyncConflictResolver
{
    private readonly ILogger<SyncConflictResolver> _logger;

    /// <summary>
    /// Retention period for deleted item tombstones (30 days).
    /// </summary>
    private static readonly TimeSpan TombstoneRetentionPeriod = TimeSpan.FromDays(30);

    public SyncConflictResolver(ILogger<SyncConflictResolver>? logger = null)
    {
        _logger = logger ?? NullLogger<SyncConflictResolver>.Instance;
    }

    /// <inheritdoc />
    public SyncData Resolve(SyncData local, SyncData remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        _logger.LogDebug(
            "Resolving sync conflict: Local ({LocalHosts} hosts, {LocalGroups} groups) vs Remote ({RemoteHosts} hosts, {RemoteGroups} groups)",
            local.Hosts.Count, local.Groups.Count, remote.Hosts.Count, remote.Groups.Count);

        var result = new SyncData
        {
            Version = "1.0",
            DeviceId = local.DeviceId,
            DeviceName = local.DeviceName,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        // Collect and filter tombstones (deletions within retention period)
        var cutoff = DateTimeOffset.UtcNow - TombstoneRetentionPeriod;
        var allDeletedItems = local.DeletedItems
            .Concat(remote.DeletedItems)
            .Where(d => d.DeletedAt > cutoff)
            .GroupBy(d => d.Id)
            .Select(g => g.OrderByDescending(d => d.DeletedAt).First())
            .ToList();

        var deletedHostIds = allDeletedItems
            .Where(d => d.ItemType == "Host")
            .Select(d => d.Id)
            .ToHashSet();

        var deletedGroupIds = allDeletedItems
            .Where(d => d.ItemType == "Group")
            .Select(d => d.Id)
            .ToHashSet();

        // Merge hosts using last-modified-wins
        var mergedHosts = MergeHosts(local.Hosts, remote.Hosts, deletedHostIds);
        result.Hosts = mergedHosts;

        // Merge groups using last-modified-wins
        var mergedGroups = MergeGroups(local.Groups, remote.Groups, deletedGroupIds);
        result.Groups = mergedGroups;

        // Keep tombstones within retention period
        result.DeletedItems = allDeletedItems;

        _logger.LogInformation(
            "Sync resolved: {HostCount} hosts, {GroupCount} groups, {DeletedCount} tombstones",
            result.Hosts.Count, result.Groups.Count, result.DeletedItems.Count);

        return result;
    }

    private List<SyncHostEntry> MergeHosts(
        List<SyncHostEntry> local,
        List<SyncHostEntry> remote,
        HashSet<Guid> deletedIds)
    {
        var merged = new Dictionary<Guid, SyncHostEntry>();

        // Add all local hosts
        foreach (var host in local)
        {
            if (!deletedIds.Contains(host.Id))
            {
                merged[host.Id] = host;
            }
        }

        // Merge remote hosts using last-modified-wins
        foreach (var host in remote)
        {
            if (deletedIds.Contains(host.Id))
            {
                continue;
            }

            if (!merged.TryGetValue(host.Id, out var existing))
            {
                // New host from remote
                merged[host.Id] = host;
                _logger.LogDebug("Added new host from remote: {HostName}", host.DisplayName);
            }
            else if (host.UpdatedAt > existing.UpdatedAt)
            {
                // Remote is newer
                merged[host.Id] = host;
                _logger.LogDebug("Updated host from remote (newer): {HostName}", host.DisplayName);
            }
        }

        return merged.Values.ToList();
    }

    private List<SyncHostGroup> MergeGroups(
        List<SyncHostGroup> local,
        List<SyncHostGroup> remote,
        HashSet<Guid> deletedIds)
    {
        var merged = new Dictionary<Guid, SyncHostGroup>();

        // Add all local groups
        foreach (var group in local)
        {
            if (!deletedIds.Contains(group.Id))
            {
                merged[group.Id] = group;
            }
        }

        // Merge remote groups
        foreach (var group in remote)
        {
            if (deletedIds.Contains(group.Id))
            {
                continue;
            }

            if (!merged.TryGetValue(group.Id, out var existing))
            {
                // New group from remote
                merged[group.Id] = group;
                _logger.LogDebug("Added new group from remote: {GroupName}", group.Name);
            }
            else if (group.CreatedAt > existing.CreatedAt)
            {
                // Remote is newer (groups don't have UpdatedAt, use CreatedAt)
                merged[group.Id] = group;
                _logger.LogDebug("Updated group from remote (newer): {GroupName}", group.Name);
            }
        }

        return merged.Values.OrderBy(g => g.SortOrder).ToList();
    }
}
