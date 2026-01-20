using SshManager.Core.Models;
using SshManager.Terminal.Controls;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service interface for managing terminal theme and font settings.
/// </summary>
/// <remarks>
/// This service handles the visual presentation of terminal themes by:
/// <list type="bullet">
/// <item><description>Converting themes to xterm.js format</description></item>
/// <item><description>Building font stacks with fallbacks for WebView2</description></item>
/// <item><description>Tracking current theme and font state</description></item>
/// </list>
/// The service uses <see cref="ThemeAdapter"/> for theme conversion and
/// <see cref="Utilities.FontStackBuilder"/> for font stack generation.
/// </remarks>
public interface ITerminalThemeManager
{
    /// <summary>
    /// Gets the currently applied terminal theme.
    /// </summary>
    TerminalTheme? CurrentTheme { get; }

    /// <summary>
    /// Gets or sets the terminal font family name.
    /// </summary>
    /// <remarks>
    /// When set, the font family is used as the preferred font with automatic
    /// fallbacks added for terminal rendering compatibility.
    /// Default value is "Cascadia Mono".
    /// </remarks>
    string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the terminal font size in pixels.
    /// </summary>
    /// <remarks>
    /// Default value is 14.0. Values less than or equal to 0 will use the default.
    /// </remarks>
    double FontSize { get; set; }

    /// <summary>
    /// Applies a terminal color theme to the specified terminal control.
    /// </summary>
    /// <param name="theme">The theme to apply.</param>
    /// <param name="terminal">The WebTerminalControl to apply the theme to.</param>
    /// <exception cref="ArgumentNullException">Thrown if theme or terminal is null.</exception>
    void ApplyTheme(TerminalTheme theme, WebTerminalControl terminal);

    /// <summary>
    /// Applies the current font settings (family and size) to the specified terminal control.
    /// </summary>
    /// <param name="terminal">The WebTerminalControl to apply font settings to.</param>
    /// <exception cref="ArgumentNullException">Thrown if terminal is null.</exception>
    void ApplyFontSettings(WebTerminalControl terminal);

    /// <summary>
    /// Resets the theme manager to default state.
    /// </summary>
    /// <remarks>
    /// Clears the current theme and resets font settings to defaults:
    /// <list type="bullet">
    /// <item><description>FontFamily: "Cascadia Mono"</description></item>
    /// <item><description>FontSize: 14.0</description></item>
    /// </list>
    /// </remarks>
    void Reset();
}
