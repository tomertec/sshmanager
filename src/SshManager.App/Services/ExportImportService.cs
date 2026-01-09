using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using SshManager.Core.Models;

namespace SshManager.App.Services;

public class ExportImportService : IExportImportService
{
    // Validation constants to prevent resource exhaustion attacks
    private const int MaxStringLength = 1000;
    private const int MaxNotesLength = 5000;
    private const int MaxItems = 10000;
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Limit maximum depth to prevent stack overflow attacks
        MaxDepth = 32
    };

    public async Task ExportAsync(string filePath, IEnumerable<HostEntry> hosts, IEnumerable<HostGroup> groups, CancellationToken ct = default)
    {
        var exportData = new ExportData
        {
            Version = "1.0",
            ExportedAt = DateTimeOffset.UtcNow,
            Groups = groups.Select(g => new ExportGroup
            {
                Id = g.Id,
                Name = g.Name,
                Icon = g.Icon,
                SortOrder = g.SortOrder,
                StatusCheckIntervalSeconds = g.StatusCheckIntervalSeconds
            }).ToList(),
            Hosts = hosts.Select(h => new ExportHost
            {
                Id = h.Id,
                DisplayName = h.DisplayName,
                Hostname = h.Hostname,
                Port = h.Port,
                Username = h.Username,
                AuthType = h.AuthType.ToString(),
                PrivateKeyPath = h.PrivateKeyPath,
                Notes = h.Notes,
                GroupId = h.GroupId
                // Password is NOT exported for security reasons (DPAPI is user-specific)
            }).ToList()
        };

        var json = JsonSerializer.Serialize(exportData, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public async Task<(List<HostEntry> Hosts, List<HostGroup> Groups)> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var exportData = JsonSerializer.Deserialize<ExportData>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse import file");

        // Validate imported data before processing
        ValidateImportData(exportData);

        // Map groups - generate new IDs to avoid conflicts
        var groupIdMap = new Dictionary<Guid, Guid>();
        var groups = exportData.Groups.Select(g =>
        {
            var newId = Guid.NewGuid();
            groupIdMap[g.Id] = newId;
            return new HostGroup
            {
                Id = newId,
                Name = g.Name,
                Icon = g.Icon,
                SortOrder = g.SortOrder,
                StatusCheckIntervalSeconds = g.StatusCheckIntervalSeconds,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }).ToList();

        // Map hosts - generate new IDs and map group references
        var hosts = exportData.Hosts.Select(h =>
        {
            var authType = Enum.TryParse<AuthType>(h.AuthType, out var at) ? at : AuthType.SshAgent;
            Guid? mappedGroupId = h.GroupId.HasValue && groupIdMap.TryGetValue(h.GroupId.Value, out var newGroupId)
                ? newGroupId
                : null;

            return new HostEntry
            {
                Id = Guid.NewGuid(),
                DisplayName = h.DisplayName,
                Hostname = h.Hostname,
                Port = h.Port,
                Username = h.Username,
                AuthType = authType,
                PrivateKeyPath = h.PrivateKeyPath,
                Notes = h.Notes,
                GroupId = mappedGroupId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
                // Password must be re-entered by user
            };
        }).ToList();

        return (hosts, groups);
    }

    /// <summary>
    /// Validates imported data to prevent security vulnerabilities and resource exhaustion.
    /// </summary>
    /// <param name="data">The deserialized export data to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    /// <exception cref="SecurityException">Thrown when security-related validation fails.</exception>
    private static void ValidateImportData(ExportData data)
    {
        // Validate item counts to prevent resource exhaustion
        if (data.Hosts.Count > MaxItems)
        {
            throw new InvalidOperationException(
                $"Import exceeds maximum host count ({MaxItems}). Found: {data.Hosts.Count}");
        }

        if (data.Groups.Count > MaxItems)
        {
            throw new InvalidOperationException(
                $"Import exceeds maximum group count ({MaxItems}). Found: {data.Groups.Count}");
        }

        // Check for duplicate host IDs (could indicate tampering)
        var hostIds = data.Hosts.Select(h => h.Id).ToHashSet();
        if (hostIds.Count != data.Hosts.Count)
        {
            throw new SecurityException("Duplicate host IDs detected in import file");
        }

        // Check for duplicate group IDs
        var groupIds = data.Groups.Select(g => g.Id).ToHashSet();
        if (groupIds.Count != data.Groups.Count)
        {
            throw new SecurityException("Duplicate group IDs detected in import file");
        }

        // Validate each host entry
        foreach (var host in data.Hosts)
        {
            ValidateHostEntry(host);
        }

        // Validate each group entry
        foreach (var group in data.Groups)
        {
            ValidateGroupEntry(group);
        }

        // Validate group references in hosts
        foreach (var host in data.Hosts)
        {
            if (host.GroupId.HasValue && !groupIds.Contains(host.GroupId.Value))
            {
                // This is a warning case - the group reference will be ignored during import
                // but we should not throw as orphaned references are handled gracefully
            }
        }
    }

    /// <summary>
    /// Validates a single host entry from import data.
    /// </summary>
    private static void ValidateHostEntry(ExportHost host)
    {
        // Validate hostname
        if (string.IsNullOrWhiteSpace(host.Hostname))
        {
            throw new InvalidOperationException("Host entry has empty hostname");
        }

        if (host.Hostname.Length > MaxStringLength)
        {
            throw new InvalidOperationException(
                $"Hostname exceeds maximum length ({MaxStringLength}): {host.Hostname[..50]}...");
        }

        // Check for null bytes in hostname (potential injection attack)
        if (host.Hostname.Contains('\0'))
        {
            throw new SecurityException("Hostname contains invalid null byte character");
        }

        // Validate port range
        if (host.Port < MinPort || host.Port > MaxPort)
        {
            throw new InvalidOperationException(
                $"Invalid port number: {host.Port}. Must be between {MinPort} and {MaxPort}");
        }

        // Validate display name length
        if (host.DisplayName?.Length > MaxStringLength)
        {
            throw new InvalidOperationException(
                $"Display name exceeds maximum length ({MaxStringLength})");
        }

        // Validate username length
        if (host.Username?.Length > MaxStringLength)
        {
            throw new InvalidOperationException(
                $"Username exceeds maximum length ({MaxStringLength})");
        }

        // Validate notes length
        if (host.Notes?.Length > MaxNotesLength)
        {
            throw new InvalidOperationException(
                $"Notes exceed maximum length ({MaxNotesLength})");
        }

        // Validate private key path
        if (host.PrivateKeyPath != null)
        {
            if (host.PrivateKeyPath.Length > MaxStringLength)
            {
                throw new InvalidOperationException(
                    $"Private key path exceeds maximum length ({MaxStringLength})");
            }

            // Check for path traversal attempts
            if (host.PrivateKeyPath.Contains(".."))
            {
                throw new SecurityException(
                    "Private key path contains path traversal sequence (..)");
            }

            // Check for null bytes
            if (host.PrivateKeyPath.Contains('\0'))
            {
                throw new SecurityException(
                    "Private key path contains invalid null byte character");
            }
        }

        // Validate auth type string
        if (!string.IsNullOrEmpty(host.AuthType) &&
            !Enum.TryParse<AuthType>(host.AuthType, ignoreCase: true, out _))
        {
            throw new InvalidOperationException(
                $"Invalid authentication type: {host.AuthType}");
        }
    }

    /// <summary>
    /// Validates a single group entry from import data.
    /// </summary>
    private static void ValidateGroupEntry(ExportGroup group)
    {
        // Validate group name
        if (string.IsNullOrWhiteSpace(group.Name))
        {
            throw new InvalidOperationException("Group entry has empty name");
        }

        if (group.Name.Length > MaxStringLength)
        {
            throw new InvalidOperationException(
                $"Group name exceeds maximum length ({MaxStringLength})");
        }

        // Validate icon length
        if (group.Icon?.Length > MaxStringLength)
        {
            throw new InvalidOperationException(
                $"Group icon exceeds maximum length ({MaxStringLength})");
        }

        // Validate sort order (prevent overflow issues)
        if (group.SortOrder < 0 || group.SortOrder > 1_000_000)
        {
            throw new InvalidOperationException(
                $"Invalid sort order: {group.SortOrder}");
        }

        // Validate status check interval
        if (group.StatusCheckIntervalSeconds < 0 || group.StatusCheckIntervalSeconds > 86400)
        {
            throw new InvalidOperationException(
                $"Invalid status check interval: {group.StatusCheckIntervalSeconds}. Must be between 0 and 86400 seconds");
        }
    }

    // Export DTOs
    private class ExportData
    {
        public string Version { get; set; } = "1.0";
        public DateTimeOffset ExportedAt { get; set; }
        public List<ExportGroup> Groups { get; set; } = [];
        public List<ExportHost> Hosts { get; set; } = [];
    }

    private class ExportGroup
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Icon { get; set; }
        public int SortOrder { get; set; }
        public int StatusCheckIntervalSeconds { get; set; } = 30;
    }

    private class ExportHost
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string Hostname { get; set; } = "";
        public int Port { get; set; }
        public string Username { get; set; } = "";
        public string AuthType { get; set; } = "SshAgent";
        public string? PrivateKeyPath { get; set; }
        public string? Notes { get; set; }
        public Guid? GroupId { get; set; }
    }
}
