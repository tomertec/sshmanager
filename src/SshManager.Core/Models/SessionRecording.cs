using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a recorded terminal session stored in ASCIINEMA v2 format.
/// </summary>
public sealed class SessionRecording
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Host that was connected during recording (nullable if host deleted)</summary>
    public Guid? HostId { get; set; }
    public HostEntry? Host { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = "";

    [Required]
    [StringLength(500)]
    public string FileName { get; set; } = "";

    public int TerminalWidth { get; set; } = 80;
    public int TerminalHeight { get; set; } = 24;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public TimeSpan Duration { get; set; }

    public long FileSizeBytes { get; set; }
    public long EventCount { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
