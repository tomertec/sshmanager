namespace SshManager.Security.OnePassword;

/// <summary>
/// Status information about the 1Password CLI and authentication state.
/// </summary>
public sealed record OnePasswordStatus(
    bool IsInstalled,
    bool IsAuthenticated,
    string? AccountEmail,
    string? AccountUrl,
    string? ErrorMessage = null);

/// <summary>
/// Represents a 1Password vault.
/// </summary>
public sealed record OnePasswordVault(string Id, string Name);

/// <summary>
/// Represents a 1Password item (summary info from listing).
/// </summary>
public sealed record OnePasswordItem(
    string Id,
    string Title,
    string Category,
    OnePasswordVault Vault,
    string[]? Tags,
    string? Url = null);

/// <summary>
/// Represents detailed information about a 1Password item including its fields.
/// </summary>
public sealed record OnePasswordItemDetail(
    string Id,
    string Title,
    string Category,
    OnePasswordVault Vault,
    IReadOnlyList<OnePasswordField> Fields);

/// <summary>
/// Represents a single field within a 1Password item.
/// </summary>
public sealed record OnePasswordField(
    string Id,
    string Label,
    string Type,
    string? Value,
    string? Reference,
    string? SectionLabel);
