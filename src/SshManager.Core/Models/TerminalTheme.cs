using System.Text.Json.Serialization;

namespace SshManager.Core.Models;

/// <summary>
/// Represents a terminal color theme with all configurable colors.
/// Supports import/export in JSON format compatible with Windows Terminal and other tools.
/// </summary>
public sealed class TerminalTheme
{
    /// <summary>
    /// Unique identifier for the theme.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the theme.
    /// </summary>
    public string Name { get; set; } = "Custom Theme";

    /// <summary>
    /// Optional author of the theme.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Whether this is a built-in theme (cannot be deleted).
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    // ===== Basic Colors =====

    /// <summary>
    /// Default foreground (text) color. Hex format: #RRGGBB
    /// </summary>
    public string Foreground { get; set; } = "#CCCCCC";

    /// <summary>
    /// Default background color. Hex format: #RRGGBB
    /// </summary>
    public string Background { get; set; } = "#0C0C0C";

    /// <summary>
    /// Cursor color. Hex format: #RRGGBB or #AARRGGBB for alpha
    /// </summary>
    public string CursorColor { get; set; } = "#CCCCCC";

    /// <summary>
    /// Text selection highlight color. Hex format: #AARRGGBB
    /// </summary>
    public string SelectionBackground { get; set; } = "#333399FF";

    // ===== ANSI Standard Colors (0-7) =====

    /// <summary>Black (ANSI 0)</summary>
    public string Black { get; set; } = "#0C0C0C";

    /// <summary>Red (ANSI 1)</summary>
    public string Red { get; set; } = "#C50F1F";

    /// <summary>Green (ANSI 2)</summary>
    public string Green { get; set; } = "#13A10E";

    /// <summary>Yellow (ANSI 3)</summary>
    public string Yellow { get; set; } = "#C19C00";

    /// <summary>Blue (ANSI 4)</summary>
    public string Blue { get; set; } = "#0037DA";

    /// <summary>Purple/Magenta (ANSI 5)</summary>
    public string Purple { get; set; } = "#881798";

    /// <summary>Cyan (ANSI 6)</summary>
    public string Cyan { get; set; } = "#3A96DD";

    /// <summary>White (ANSI 7)</summary>
    public string White { get; set; } = "#CCCCCC";

    // ===== ANSI Bright Colors (8-15) =====

    /// <summary>Bright Black (ANSI 8)</summary>
    public string BrightBlack { get; set; } = "#767676";

    /// <summary>Bright Red (ANSI 9)</summary>
    public string BrightRed { get; set; } = "#E74856";

    /// <summary>Bright Green (ANSI 10)</summary>
    public string BrightGreen { get; set; } = "#16C60C";

    /// <summary>Bright Yellow (ANSI 11)</summary>
    public string BrightYellow { get; set; } = "#F9F1A5";

    /// <summary>Bright Blue (ANSI 12)</summary>
    public string BrightBlue { get; set; } = "#3B78FF";

    /// <summary>Bright Purple/Magenta (ANSI 13)</summary>
    public string BrightPurple { get; set; } = "#B4009E";

    /// <summary>Bright Cyan (ANSI 14)</summary>
    public string BrightCyan { get; set; } = "#61D6D6";

    /// <summary>Bright White (ANSI 15)</summary>
    public string BrightWhite { get; set; } = "#F2F2F2";

    // ===== UI Colors (for terminal chrome) =====

    /// <summary>Search match highlight color. Hex format: #AARRGGBB</summary>
    public string SearchMatchBackground { get; set; } = "#64FFC800";

    /// <summary>Current search match highlight color. Hex format: #AARRGGBB</summary>
    public string SearchCurrentMatchBackground { get; set; } = "#B4FF8C00";

    /// <summary>
    /// Creates a deep copy of this theme.
    /// </summary>
    public TerminalTheme Clone()
    {
        return new TerminalTheme
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name + " (Copy)",
            Author = Author,
            IsBuiltIn = false,
            Foreground = Foreground,
            Background = Background,
            CursorColor = CursorColor,
            SelectionBackground = SelectionBackground,
            Black = Black,
            Red = Red,
            Green = Green,
            Yellow = Yellow,
            Blue = Blue,
            Purple = Purple,
            Cyan = Cyan,
            White = White,
            BrightBlack = BrightBlack,
            BrightRed = BrightRed,
            BrightGreen = BrightGreen,
            BrightYellow = BrightYellow,
            BrightBlue = BrightBlue,
            BrightPurple = BrightPurple,
            BrightCyan = BrightCyan,
            BrightWhite = BrightWhite,
            SearchMatchBackground = SearchMatchBackground,
            SearchCurrentMatchBackground = SearchCurrentMatchBackground
        };
    }
}
