namespace SshManager.Core.Models;

/// <summary>
/// Represents a command that was executed in a terminal session.
/// Used for command history and autocompletion suggestions.
/// </summary>
public sealed class CommandHistoryEntry
{
    /// <summary>
    /// Unique identifier for this history entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The host where this command was executed (null for general history).
    /// </summary>
    public Guid? HostId { get; set; }

    /// <summary>
    /// The command text that was executed.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// When this command was last executed.
    /// </summary>
    public DateTimeOffset ExecutedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of times this command has been used.
    /// </summary>
    public int UseCount { get; set; } = 1;
}
