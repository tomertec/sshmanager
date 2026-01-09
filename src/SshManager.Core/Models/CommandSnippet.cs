namespace SshManager.Core.Models;

/// <summary>
/// Represents a reusable command snippet/macro.
/// </summary>
public sealed class CommandSnippet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the snippet.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The command text to execute.
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Optional description of what this command does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional category for grouping snippets.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Sort order for display (lower numbers appear first).
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// When this snippet was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this snippet was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
