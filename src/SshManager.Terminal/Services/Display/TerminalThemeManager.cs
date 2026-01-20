using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Services;

/// <summary>
/// Default implementation of <see cref="ITerminalThemeManager"/> that manages
/// terminal theme and font settings.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates the logic for applying themes and fonts to terminal controls:
/// </para>
/// <list type="bullet">
/// <item><description>Theme conversion using <see cref="ThemeAdapter.ToXtermTheme"/></description></item>
/// <item><description>Font stack building using <see cref="FontStackBuilder.Build"/></description></item>
/// <item><description>State tracking for current theme and font settings</description></item>
/// </list>
/// <para>
/// <b>Thread safety:</b> This class is not thread-safe. All methods that interact with
/// the terminal control must be called on the UI thread.
/// </para>
/// </remarks>
public sealed class TerminalThemeManager : ITerminalThemeManager
{
    private const string DefaultFontFamily = "Cascadia Mono";
    private const double DefaultFontSize = 14.0;

    private readonly ILogger<TerminalThemeManager> _logger;

    private TerminalTheme? _currentTheme;
    private string _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalThemeManager"/> class.
    /// </summary>
    public TerminalThemeManager()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalThemeManager"/> class
    /// with an optional logger.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public TerminalThemeManager(ILogger<TerminalThemeManager>? logger)
    {
        _logger = logger ?? NullLogger<TerminalThemeManager>.Instance;
    }

    /// <inheritdoc />
    public TerminalTheme? CurrentTheme => _currentTheme;

    /// <inheritdoc />
    public string FontFamily
    {
        get => _fontFamily;
        set => _fontFamily = string.IsNullOrWhiteSpace(value) ? DefaultFontFamily : value;
    }

    /// <inheritdoc />
    public double FontSize
    {
        get => _fontSize;
        set => _fontSize = value > 0 ? value : DefaultFontSize;
    }

    /// <inheritdoc />
    public void ApplyTheme(TerminalTheme theme, WebTerminalControl terminal)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(terminal);

        try
        {
            _currentTheme = theme;

            // Convert to xterm.js theme format using the existing ThemeAdapter
            var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

            // Apply to WebTerminalControl
            terminal.SetTheme(xtermTheme);

            _logger.LogDebug("Applied theme: {ThemeName}", theme.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply theme: {ThemeName}", theme.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public void ApplyFontSettings(WebTerminalControl terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        try
        {
            // Use defaults if current values are invalid
            var fontFamily = string.IsNullOrWhiteSpace(_fontFamily)
                ? DefaultFontFamily
                : _fontFamily;
            var fontSize = _fontSize > 0 ? _fontSize : DefaultFontSize;

            // Build font stack with fallbacks for cross-platform compatibility
            var fontStack = FontStackBuilder.Build(fontFamily);

            // Apply to terminal
            terminal.SetFont(fontStack, fontSize);

            _logger.LogDebug("Applied font settings: {FontFamily} at {FontSize}px", fontFamily, fontSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply font settings");
            throw;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _currentTheme = null;
        _fontFamily = DefaultFontFamily;
        _fontSize = DefaultFontSize;

        _logger.LogDebug("Theme manager reset to defaults");
    }
}
