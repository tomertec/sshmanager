using System.IO;
using System.Text.Json;
using SshManager.Core.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for managing terminal color themes, including built-in themes
/// and import/export functionality.
/// </summary>
public interface ITerminalThemeService
{
    /// <summary>
    /// Gets all available themes (built-in and custom).
    /// </summary>
    IReadOnlyList<TerminalTheme> GetAllThemes();

    /// <summary>
    /// Gets a theme by its ID.
    /// </summary>
    TerminalTheme? GetTheme(string id);

    /// <summary>
    /// Gets a theme by its name.
    /// </summary>
    TerminalTheme? GetThemeByName(string name);

    /// <summary>
    /// Gets all built-in themes.
    /// </summary>
    IReadOnlyList<TerminalTheme> GetBuiltInThemes();

    /// <summary>
    /// Adds a custom theme.
    /// </summary>
    void AddCustomTheme(TerminalTheme theme);

    /// <summary>
    /// Removes a custom theme by ID.
    /// </summary>
    bool RemoveCustomTheme(string id);

    /// <summary>
    /// Exports a theme to JSON string.
    /// </summary>
    string ExportTheme(TerminalTheme theme);

    /// <summary>
    /// Exports a theme to a file.
    /// </summary>
    Task ExportThemeToFileAsync(TerminalTheme theme, string filePath);

    /// <summary>
    /// Imports a theme from JSON string.
    /// </summary>
    TerminalTheme? ImportTheme(string json);

    /// <summary>
    /// Imports a theme from a file.
    /// </summary>
    Task<TerminalTheme?> ImportThemeFromFileAsync(string filePath);

    /// <summary>
    /// Gets custom themes stored in the user's data directory.
    /// </summary>
    IReadOnlyList<TerminalTheme> GetCustomThemes();

    /// <summary>
    /// Saves custom themes to disk.
    /// </summary>
    Task SaveCustomThemesAsync();

    /// <summary>
    /// Loads custom themes from disk.
    /// </summary>
    Task LoadCustomThemesAsync();

    /// <summary>
    /// Event fired when themes are changed.
    /// </summary>
    event EventHandler? ThemesChanged;
}

/// <summary>
/// Implementation of terminal theme management service.
/// </summary>
public sealed class TerminalThemeService : ITerminalThemeService
{
    private readonly List<TerminalTheme> _builtInThemes;
    private readonly List<TerminalTheme> _customThemes = new();
    private readonly string _customThemesPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event EventHandler? ThemesChanged;

    public TerminalThemeService()
    {
        _builtInThemes = CreateBuiltInThemes();

        // Custom themes are stored in %LocalAppData%\SshManager\themes.json
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SshManager");
        Directory.CreateDirectory(appDataPath);
        _customThemesPath = Path.Combine(appDataPath, "terminal-themes.json");
    }

    public IReadOnlyList<TerminalTheme> GetAllThemes()
    {
        return _builtInThemes.Concat(_customThemes).ToList();
    }

    public TerminalTheme? GetTheme(string id)
    {
        return _builtInThemes.FirstOrDefault(t => t.Id == id)
            ?? _customThemes.FirstOrDefault(t => t.Id == id);
    }

    public TerminalTheme? GetThemeByName(string name)
    {
        return _builtInThemes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? _customThemes.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<TerminalTheme> GetBuiltInThemes() => _builtInThemes;

    public IReadOnlyList<TerminalTheme> GetCustomThemes() => _customThemes;

    public void AddCustomTheme(TerminalTheme theme)
    {
        theme.IsBuiltIn = false;
        _customThemes.Add(theme);
        ThemesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveCustomTheme(string id)
    {
        var theme = _customThemes.FirstOrDefault(t => t.Id == id);
        if (theme != null)
        {
            _customThemes.Remove(theme);
            ThemesChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public string ExportTheme(TerminalTheme theme)
    {
        return JsonSerializer.Serialize(theme, JsonOptions);
    }

    public async Task ExportThemeToFileAsync(TerminalTheme theme, string filePath)
    {
        var json = ExportTheme(theme);
        await File.WriteAllTextAsync(filePath, json);
    }

    public TerminalTheme? ImportTheme(string json)
    {
        try
        {
            var theme = JsonSerializer.Deserialize<TerminalTheme>(json, JsonOptions);
            if (theme != null)
            {
                // Generate new ID to avoid conflicts
                theme.Id = Guid.NewGuid().ToString();
                theme.IsBuiltIn = false;
            }
            return theme;
        }
        catch
        {
            return null;
        }
    }

    public async Task<TerminalTheme?> ImportThemeFromFileAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return ImportTheme(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveCustomThemesAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_customThemes, JsonOptions);
            await File.WriteAllTextAsync(_customThemesPath, json);
        }
        catch
        {
            // Silently fail - themes are not critical
        }
    }

    public async Task LoadCustomThemesAsync()
    {
        try
        {
            if (File.Exists(_customThemesPath))
            {
                var json = await File.ReadAllTextAsync(_customThemesPath);
                var themes = JsonSerializer.Deserialize<List<TerminalTheme>>(json, JsonOptions);
                if (themes != null)
                {
                    _customThemes.Clear();
                    foreach (var theme in themes)
                    {
                        theme.IsBuiltIn = false;
                        _customThemes.Add(theme);
                    }
                    ThemesChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch
        {
            // Silently fail - use default themes
        }
    }

    private static List<TerminalTheme> CreateBuiltInThemes()
    {
        return new List<TerminalTheme>
        {
            CreateDefaultTheme(),
            CreateDraculaTheme(),
            CreateNordTheme(),
            CreateSolarizedDarkTheme(),
            CreateSolarizedLightTheme(),
            CreateOneDarkTheme(),
            CreateMonokaiTheme(),
            CreateGruvboxDarkTheme(),
            CreateTokyoNightTheme()
        };
    }

    private static TerminalTheme CreateDefaultTheme()
    {
        return new TerminalTheme
        {
            Id = "default",
            Name = "Default",
            Author = "SshManager",
            IsBuiltIn = true,
            Foreground = "#CCCCCC",
            Background = "#0C0C0C",
            CursorColor = "#CCCCCC",
            SelectionBackground = "#333399FF",
            Black = "#0C0C0C",
            Red = "#C50F1F",
            Green = "#13A10E",
            Yellow = "#C19C00",
            Blue = "#0037DA",
            Purple = "#881798",
            Cyan = "#3A96DD",
            White = "#CCCCCC",
            BrightBlack = "#767676",
            BrightRed = "#E74856",
            BrightGreen = "#16C60C",
            BrightYellow = "#F9F1A5",
            BrightBlue = "#3B78FF",
            BrightPurple = "#B4009E",
            BrightCyan = "#61D6D6",
            BrightWhite = "#F2F2F2",
            SearchMatchBackground = "#64FFC800",
            SearchCurrentMatchBackground = "#B4FF8C00"
        };
    }

    private static TerminalTheme CreateDraculaTheme()
    {
        return new TerminalTheme
        {
            Id = "dracula",
            Name = "Dracula",
            Author = "Zeno Rocha",
            IsBuiltIn = true,
            Foreground = "#F8F8F2",
            Background = "#282A36",
            CursorColor = "#F8F8F2",
            SelectionBackground = "#4044528A",
            Black = "#21222C",
            Red = "#FF5555",
            Green = "#50FA7B",
            Yellow = "#F1FA8C",
            Blue = "#BD93F9",
            Purple = "#FF79C6",
            Cyan = "#8BE9FD",
            White = "#F8F8F2",
            BrightBlack = "#6272A4",
            BrightRed = "#FF6E6E",
            BrightGreen = "#69FF94",
            BrightYellow = "#FFFFA5",
            BrightBlue = "#D6ACFF",
            BrightPurple = "#FF92DF",
            BrightCyan = "#A4FFFF",
            BrightWhite = "#FFFFFF",
            SearchMatchBackground = "#64F1FA8C",
            SearchCurrentMatchBackground = "#B4FF79C6"
        };
    }

    private static TerminalTheme CreateNordTheme()
    {
        return new TerminalTheme
        {
            Id = "nord",
            Name = "Nord",
            Author = "Arctic Ice Studio",
            IsBuiltIn = true,
            Foreground = "#D8DEE9",
            Background = "#2E3440",
            CursorColor = "#D8DEE9",
            SelectionBackground = "#434C5E8A",
            Black = "#3B4252",
            Red = "#BF616A",
            Green = "#A3BE8C",
            Yellow = "#EBCB8B",
            Blue = "#81A1C1",
            Purple = "#B48EAD",
            Cyan = "#88C0D0",
            White = "#E5E9F0",
            BrightBlack = "#4C566A",
            BrightRed = "#BF616A",
            BrightGreen = "#A3BE8C",
            BrightYellow = "#EBCB8B",
            BrightBlue = "#81A1C1",
            BrightPurple = "#B48EAD",
            BrightCyan = "#8FBCBB",
            BrightWhite = "#ECEFF4",
            SearchMatchBackground = "#64EBCB8B",
            SearchCurrentMatchBackground = "#B488C0D0"
        };
    }

    private static TerminalTheme CreateSolarizedDarkTheme()
    {
        return new TerminalTheme
        {
            Id = "solarized-dark",
            Name = "Solarized Dark",
            Author = "Ethan Schoonover",
            IsBuiltIn = true,
            Foreground = "#839496",
            Background = "#002B36",
            CursorColor = "#839496",
            SelectionBackground = "#073642CC",
            Black = "#073642",
            Red = "#DC322F",
            Green = "#859900",
            Yellow = "#B58900",
            Blue = "#268BD2",
            Purple = "#D33682",
            Cyan = "#2AA198",
            White = "#EEE8D5",
            BrightBlack = "#002B36",
            BrightRed = "#CB4B16",
            BrightGreen = "#586E75",
            BrightYellow = "#657B83",
            BrightBlue = "#839496",
            BrightPurple = "#6C71C4",
            BrightCyan = "#93A1A1",
            BrightWhite = "#FDF6E3",
            SearchMatchBackground = "#64B58900",
            SearchCurrentMatchBackground = "#B4268BD2"
        };
    }

    private static TerminalTheme CreateSolarizedLightTheme()
    {
        return new TerminalTheme
        {
            Id = "solarized-light",
            Name = "Solarized Light",
            Author = "Ethan Schoonover",
            IsBuiltIn = true,
            Foreground = "#657B83",
            Background = "#FDF6E3",
            CursorColor = "#657B83",
            SelectionBackground = "#EEE8D5CC",
            Black = "#073642",
            Red = "#DC322F",
            Green = "#859900",
            Yellow = "#B58900",
            Blue = "#268BD2",
            Purple = "#D33682",
            Cyan = "#2AA198",
            White = "#EEE8D5",
            BrightBlack = "#002B36",
            BrightRed = "#CB4B16",
            BrightGreen = "#586E75",
            BrightYellow = "#657B83",
            BrightBlue = "#839496",
            BrightPurple = "#6C71C4",
            BrightCyan = "#93A1A1",
            BrightWhite = "#FDF6E3",
            SearchMatchBackground = "#64B58900",
            SearchCurrentMatchBackground = "#B4268BD2"
        };
    }

    private static TerminalTheme CreateOneDarkTheme()
    {
        return new TerminalTheme
        {
            Id = "one-dark",
            Name = "One Dark",
            Author = "Atom",
            IsBuiltIn = true,
            Foreground = "#ABB2BF",
            Background = "#282C34",
            CursorColor = "#528BFF",
            SelectionBackground = "#3E44518A",
            Black = "#282C34",
            Red = "#E06C75",
            Green = "#98C379",
            Yellow = "#E5C07B",
            Blue = "#61AFEF",
            Purple = "#C678DD",
            Cyan = "#56B6C2",
            White = "#ABB2BF",
            BrightBlack = "#5C6370",
            BrightRed = "#E06C75",
            BrightGreen = "#98C379",
            BrightYellow = "#E5C07B",
            BrightBlue = "#61AFEF",
            BrightPurple = "#C678DD",
            BrightCyan = "#56B6C2",
            BrightWhite = "#FFFFFF",
            SearchMatchBackground = "#64E5C07B",
            SearchCurrentMatchBackground = "#B461AFEF"
        };
    }

    private static TerminalTheme CreateMonokaiTheme()
    {
        return new TerminalTheme
        {
            Id = "monokai",
            Name = "Monokai",
            Author = "Wimer Hazenberg",
            IsBuiltIn = true,
            Foreground = "#F8F8F2",
            Background = "#272822",
            CursorColor = "#F8F8F2",
            SelectionBackground = "#49483E8A",
            Black = "#272822",
            Red = "#F92672",
            Green = "#A6E22E",
            Yellow = "#F4BF75",
            Blue = "#66D9EF",
            Purple = "#AE81FF",
            Cyan = "#A1EFE4",
            White = "#F8F8F2",
            BrightBlack = "#75715E",
            BrightRed = "#F92672",
            BrightGreen = "#A6E22E",
            BrightYellow = "#F4BF75",
            BrightBlue = "#66D9EF",
            BrightPurple = "#AE81FF",
            BrightCyan = "#A1EFE4",
            BrightWhite = "#F9F8F5",
            SearchMatchBackground = "#64F4BF75",
            SearchCurrentMatchBackground = "#B4F92672"
        };
    }

    private static TerminalTheme CreateGruvboxDarkTheme()
    {
        return new TerminalTheme
        {
            Id = "gruvbox-dark",
            Name = "Gruvbox Dark",
            Author = "Pavel Pertsev",
            IsBuiltIn = true,
            Foreground = "#EBDBB2",
            Background = "#282828",
            CursorColor = "#EBDBB2",
            SelectionBackground = "#3C38368A",
            Black = "#282828",
            Red = "#CC241D",
            Green = "#98971A",
            Yellow = "#D79921",
            Blue = "#458588",
            Purple = "#B16286",
            Cyan = "#689D6A",
            White = "#A89984",
            BrightBlack = "#928374",
            BrightRed = "#FB4934",
            BrightGreen = "#B8BB26",
            BrightYellow = "#FABD2F",
            BrightBlue = "#83A598",
            BrightPurple = "#D3869B",
            BrightCyan = "#8EC07C",
            BrightWhite = "#EBDBB2",
            SearchMatchBackground = "#64D79921",
            SearchCurrentMatchBackground = "#B4FB4934"
        };
    }

    private static TerminalTheme CreateTokyoNightTheme()
    {
        return new TerminalTheme
        {
            Id = "tokyo-night",
            Name = "Tokyo Night",
            Author = "enkia",
            IsBuiltIn = true,
            Foreground = "#A9B1D6",
            Background = "#1A1B26",
            CursorColor = "#C0CAF5",
            SelectionBackground = "#33467C8A",
            Black = "#15161E",
            Red = "#F7768E",
            Green = "#9ECE6A",
            Yellow = "#E0AF68",
            Blue = "#7AA2F7",
            Purple = "#BB9AF7",
            Cyan = "#7DCFFF",
            White = "#A9B1D6",
            BrightBlack = "#414868",
            BrightRed = "#F7768E",
            BrightGreen = "#9ECE6A",
            BrightYellow = "#E0AF68",
            BrightBlue = "#7AA2F7",
            BrightPurple = "#BB9AF7",
            BrightCyan = "#7DCFFF",
            BrightWhite = "#C0CAF5",
            SearchMatchBackground = "#64E0AF68",
            SearchCurrentMatchBackground = "#B47AA2F7"
        };
    }
}
