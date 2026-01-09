namespace SshManager.Core;

/// <summary>
/// Provides a predefined color palette for host groups with friendly names.
/// Colors are designed to work well with dark themes.
/// </summary>
public static class GroupColors
{
    /// <summary>
    /// Represents a color option with a hex value and friendly name.
    /// </summary>
    public sealed record ColorOption(string? HexValue, string Name, string DisplayName);

    /// <summary>
    /// Gets the default (no color) option.
    /// </summary>
    public static ColorOption None { get; } = new ColorOption(null, "None", "None / Default");

    /// <summary>
    /// Gets all predefined color options including the None option.
    /// </summary>
    public static IReadOnlyList<ColorOption> All { get; } = new[]
    {
        None,
        new ColorOption("#E74C3C", "Red", "Red - Production"),
        new ColorOption("#E67E22", "Orange", "Orange - Staging"),
        new ColorOption("#27AE60", "Green", "Green - Development"),
        new ColorOption("#3498DB", "Blue", "Blue - Testing"),
        new ColorOption("#9B59B6", "Purple", "Purple - Internal"),
        new ColorOption("#1ABC9C", "Teal", "Teal - External"),
        new ColorOption("#95A5A6", "Gray", "Gray - Archive"),
        new ColorOption("#F39C12", "Amber", "Amber - Sandbox"),
        new ColorOption("#E91E63", "Pink", "Pink - Special"),
        new ColorOption("#00BCD4", "Cyan", "Cyan - Cloud"),
        new ColorOption("#8BC34A", "LimeGreen", "Lime - QA")
    };

    /// <summary>
    /// Gets a color option by its hex value.
    /// Returns None if the hex value is not found.
    /// </summary>
    public static ColorOption GetByHexValue(string? hexValue)
    {
        if (string.IsNullOrWhiteSpace(hexValue))
        {
            return None;
        }

        return All.FirstOrDefault(c => c.HexValue?.Equals(hexValue, StringComparison.OrdinalIgnoreCase) == true)
            ?? None;
    }
}
