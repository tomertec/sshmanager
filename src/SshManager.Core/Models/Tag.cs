using System.ComponentModel.DataAnnotations;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a tag that can be applied to SSH host entries for categorization and filtering.
/// </summary>
public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the tag.
    /// </summary>
    [Required(ErrorMessage = "Tag name is required")]
    [StringLength(50, ErrorMessage = "Tag name cannot exceed 50 characters")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional color for the tag in hex format (e.g., "#FF5733").
    /// Null or empty string means no color (default).
    /// </summary>
    [StringLength(7, ErrorMessage = "Color must be in hex format (e.g., #FF5733)")]
    [RegularExpression(@"^$|^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be in hex format (e.g., #FF5733) or empty")]
    public string? Color { get; set; }

    /// <summary>
    /// Hosts that have this tag applied.
    /// </summary>
    public ICollection<HostEntry> Hosts { get; set; } = new List<HostEntry>();

    /// <summary>
    /// When this tag was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
