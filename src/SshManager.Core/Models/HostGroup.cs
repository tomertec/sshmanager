namespace SshManager.Core.Models;

/// <summary>
/// Represents a folder/group for organizing hosts.
/// </summary>
public sealed class HostGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the group.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional icon name (for WPF-UI icon display).
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Sort order for display (lower numbers appear first).
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Status check interval for hosts in this group, in seconds.
    /// </summary>
    public int StatusCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Optional color for the group in hex format (e.g., "#FF5733").
    /// Null or empty string means no color (default).
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Hosts belonging to this group.
    /// </summary>
    public List<HostEntry> Hosts { get; set; } = [];

    /// <summary>
    /// When this group was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
