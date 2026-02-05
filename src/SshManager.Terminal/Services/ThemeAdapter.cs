using System.Windows.Media;
using SshManager.Core.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Adapter for converting SshManager terminal themes to xterm.js theme format.
/// </summary>
public static class ThemeAdapter
{
    /// <summary>
    /// Converts a SshManager TerminalTheme to xterm.js theme format.
    /// </summary>
    /// <param name="theme">The SshManager theme to convert.</param>
    /// <returns>A dictionary with xterm.js theme properties as hex color strings.</returns>
    public static Dictionary<string, string> ToXtermTheme(TerminalTheme theme)
    {
        if (theme == null) throw new ArgumentNullException(nameof(theme));

        return new Dictionary<string, string>
        {
            ["background"] = EnsureHexFormat(theme.Background),
            ["foreground"] = EnsureHexFormat(theme.Foreground),
            ["cursor"] = EnsureHexFormat(theme.Foreground),
            ["cursorAccent"] = EnsureHexFormat(theme.Background),
            ["selectionBackground"] = EnsureHexFormat(theme.SelectionBackground),

            // Standard ANSI colors (0-7)
            ["black"] = EnsureHexFormat(theme.Black),
            ["red"] = EnsureHexFormat(theme.Red),
            ["green"] = EnsureHexFormat(theme.Green),
            ["yellow"] = EnsureHexFormat(theme.Yellow),
            ["blue"] = EnsureHexFormat(theme.Blue),
            ["magenta"] = EnsureHexFormat(theme.Purple),
            ["cyan"] = EnsureHexFormat(theme.Cyan),
            ["white"] = EnsureHexFormat(theme.White),

            // Bright ANSI colors (8-15)
            ["brightBlack"] = EnsureHexFormat(theme.BrightBlack),
            ["brightRed"] = EnsureHexFormat(theme.BrightRed),
            ["brightGreen"] = EnsureHexFormat(theme.BrightGreen),
            ["brightYellow"] = EnsureHexFormat(theme.BrightYellow),
            ["brightBlue"] = EnsureHexFormat(theme.BrightBlue),
            ["brightMagenta"] = EnsureHexFormat(theme.BrightPurple),
            ["brightCyan"] = EnsureHexFormat(theme.BrightCyan),
            ["brightWhite"] = EnsureHexFormat(theme.BrightWhite)
        };
    }

    /// <summary>
    /// Creates a default dark theme dictionary for xterm.js.
    /// </summary>
    public static Dictionary<string, string> CreateDefaultDarkXtermTheme()
    {
        return new Dictionary<string, string>
        {
            ["background"] = TerminalConstants.ThemeColors.Background,
            ["foreground"] = TerminalConstants.ThemeColors.Foreground,
            ["cursor"] = TerminalConstants.ThemeColors.Cursor,
            ["cursorAccent"] = TerminalConstants.ThemeColors.CursorAccent,
            ["selectionBackground"] = TerminalConstants.ThemeColors.SelectionBackground,

            // Standard ANSI colors (0-7)
            ["black"] = TerminalConstants.ThemeColors.Black,
            ["red"] = TerminalConstants.ThemeColors.Red,
            ["green"] = TerminalConstants.ThemeColors.Green,
            ["yellow"] = TerminalConstants.ThemeColors.Yellow,
            ["blue"] = TerminalConstants.ThemeColors.Blue,
            ["magenta"] = TerminalConstants.ThemeColors.Magenta,
            ["cyan"] = TerminalConstants.ThemeColors.Cyan,
            ["white"] = TerminalConstants.ThemeColors.White,

            // Bright ANSI colors (8-15)
            ["brightBlack"] = TerminalConstants.ThemeColors.BrightBlack,
            ["brightRed"] = TerminalConstants.ThemeColors.BrightRed,
            ["brightGreen"] = TerminalConstants.ThemeColors.BrightGreen,
            ["brightYellow"] = TerminalConstants.ThemeColors.BrightYellow,
            ["brightBlue"] = TerminalConstants.ThemeColors.BrightBlue,
            ["brightMagenta"] = TerminalConstants.ThemeColors.BrightMagenta,
            ["brightCyan"] = TerminalConstants.ThemeColors.BrightCyan,
            ["brightWhite"] = TerminalConstants.ThemeColors.BrightWhite
        };
    }

    /// <summary>
    /// Ensures a color string is in hex format with # prefix.
    /// </summary>
    /// <param name="color">The color string (may or may not have # prefix).</param>
    /// <returns>Hex color string with # prefix (e.g., "#1E1E1E").</returns>
    private static string EnsureHexFormat(string color)
    {
        if (string.IsNullOrEmpty(color))
            return TerminalConstants.ThemeColors.FallbackColor;

        // If already has #, return as-is
        if (color.StartsWith("#"))
            return color;

        // Add # prefix
        return $"#{color}";
    }

    /// <summary>
    /// Converts a Color to a hex string for xterm.js.
    /// </summary>
    /// <param name="color">The color to convert.</param>
    /// <returns>Hex color string (e.g., "#FF0000").</returns>
    public static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
