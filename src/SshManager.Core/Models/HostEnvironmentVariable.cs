using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace SshManager.Core.Models;

/// <summary>
/// Represents an environment variable to be set when connecting to an SSH host.
/// </summary>
public sealed partial class HostEnvironmentVariable : IValidatableObject
{
    /// <summary>
    /// Maximum length for environment variable name.
    /// </summary>
    private const int MaxNameLength = Constants.StringLimits.MaxEnvironmentVariableNameLength;

    /// <summary>
    /// Maximum length for environment variable value.
    /// </summary>
    private const int MaxValueLength = Constants.StringLimits.MaxEnvironmentVariableValueLength;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The host entry this environment variable belongs to.
    /// </summary>
    public Guid HostEntryId { get; set; }

    /// <summary>
    /// The name of the environment variable (e.g., "MY_VAR", "EDITOR").
    /// Must follow POSIX naming rules: start with letter or underscore,
    /// followed by letters, digits, or underscores.
    /// </summary>
    [Required(ErrorMessage = "Environment variable name is required")]
    [StringLength(MaxNameLength, ErrorMessage = "Environment variable name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The value of the environment variable.
    /// </summary>
    [StringLength(MaxValueLength, ErrorMessage = "Environment variable value cannot exceed 1000 characters")]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Whether this environment variable is enabled.
    /// Disabled variables are not sent during SSH connection.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional description explaining the purpose of this variable.
    /// </summary>
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }

    /// <summary>
    /// Sort order for display and processing order (lower numbers first).
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// When this environment variable was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this environment variable was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the host entry.
    /// </summary>
    public HostEntry Host { get; set; } = null!;

    /// <summary>
    /// Validates the environment variable beyond simple data annotations.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate name is not empty or whitespace
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                "Environment variable name cannot be empty or whitespace",
                [nameof(Name)]);
            yield break;
        }

        // Validate name follows POSIX naming convention
        // Must start with letter or underscore, followed by letters, digits, or underscores
        if (!PosixNameRegex().IsMatch(Name))
        {
            yield return new ValidationResult(
                "Environment variable name must start with a letter or underscore, " +
                "followed by letters, digits, or underscores (POSIX naming convention)",
                [nameof(Name)]);
        }
    }

    /// <summary>
    /// Regex for validating POSIX-compliant environment variable names.
    /// Must start with letter or underscore, followed by letters, digits, or underscores.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex PosixNameRegex();
}
