using System.Windows.Media;
using FluentAssertions;
using SshManager.Core.Models;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Tests.Services;

/// <summary>
/// Unit tests for ThemeAdapter - color conversion for xterm.js.
/// </summary>
public class ThemeAdapterTests
{
    #region ToXtermTheme Tests

    [Fact]
    public void ToXtermTheme_ConvertsForegroundCorrectly()
    {
        var theme = new TerminalTheme
        {
            Foreground = "#FFFFFF",
            Background = "#000000"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["foreground"].Should().Be("#FFFFFF");
    }

    [Fact]
    public void ToXtermTheme_ConvertsBackgroundCorrectly()
    {
        var theme = new TerminalTheme
        {
            Background = "#1E1E1E",
            Foreground = "#FFFFFF"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["background"].Should().Be("#1E1E1E");
    }

    [Fact]
    public void ToXtermTheme_ConvertsSelectionBackgroundCorrectly()
    {
        var theme = new TerminalTheme
        {
            SelectionBackground = "#3399FF",
            Background = "#000000",
            Foreground = "#FFFFFF"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["selectionBackground"].Should().Be("#3399FF");
    }

    [Fact]
    public void ToXtermTheme_SetsCursorToForeground()
    {
        var theme = new TerminalTheme
        {
            Foreground = "#CCCCCC",
            Background = "#000000"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["cursor"].Should().Be("#CCCCCC");
    }

    [Fact]
    public void ToXtermTheme_SetsCursorAccentToBackground()
    {
        var theme = new TerminalTheme
        {
            Background = "#0C0C0C",
            Foreground = "#FFFFFF"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["cursorAccent"].Should().Be("#0C0C0C");
    }

    [Fact]
    public void ToXtermTheme_AddsHashPrefixIfMissing()
    {
        var theme = new TerminalTheme
        {
            Foreground = "FFFFFF", // Missing # prefix
            Background = "000000"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["foreground"].Should().Be("#FFFFFF");
        xtermTheme["background"].Should().Be("#000000");
    }

    [Fact]
    public void ToXtermTheme_PreservesHashPrefix()
    {
        var theme = new TerminalTheme
        {
            Foreground = "#AABBCC",
            Background = "#112233"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["foreground"].Should().Be("#AABBCC");
        xtermTheme["background"].Should().Be("#112233");
    }

    [Fact]
    public void ToXtermTheme_HandlesEmptyColor()
    {
        var theme = new TerminalTheme
        {
            Foreground = "",
            Background = "#000000"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["foreground"].Should().Be("#000000"); // Default to black
    }

    [Fact]
    public void ToXtermTheme_HandlesNullColor()
    {
        var theme = new TerminalTheme
        {
            Foreground = null!,
            Background = "#000000"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["foreground"].Should().Be("#000000"); // Default to black
    }

    [Fact]
    public void ToXtermTheme_ConvertsAllStandardColors()
    {
        var theme = new TerminalTheme
        {
            Foreground = "#FFFFFF",
            Background = "#000000",
            Black = "#0C0C0C",
            Red = "#C50F1F",
            Green = "#13A10E",
            Yellow = "#C19C00",
            Blue = "#0037DA",
            Purple = "#881798",
            Cyan = "#3A96DD",
            White = "#CCCCCC"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["black"].Should().Be("#0C0C0C");
        xtermTheme["red"].Should().Be("#C50F1F");
        xtermTheme["green"].Should().Be("#13A10E");
        xtermTheme["yellow"].Should().Be("#C19C00");
        xtermTheme["blue"].Should().Be("#0037DA");
        xtermTheme["magenta"].Should().Be("#881798"); // Purple maps to magenta
        xtermTheme["cyan"].Should().Be("#3A96DD");
        xtermTheme["white"].Should().Be("#CCCCCC");
    }

    [Fact]
    public void ToXtermTheme_ConvertsAllBrightColors()
    {
        var theme = new TerminalTheme
        {
            Foreground = "#FFFFFF",
            Background = "#000000",
            BrightBlack = "#767676",
            BrightRed = "#E74856",
            BrightGreen = "#16C60C",
            BrightYellow = "#F9F1A5",
            BrightBlue = "#3B78FF",
            BrightPurple = "#B4009E",
            BrightCyan = "#61D6D6",
            BrightWhite = "#F2F2F2"
        };

        var xtermTheme = ThemeAdapter.ToXtermTheme(theme);

        xtermTheme["brightBlack"].Should().Be("#767676");
        xtermTheme["brightRed"].Should().Be("#E74856");
        xtermTheme["brightGreen"].Should().Be("#16C60C");
        xtermTheme["brightYellow"].Should().Be("#F9F1A5");
        xtermTheme["brightBlue"].Should().Be("#3B78FF");
        xtermTheme["brightMagenta"].Should().Be("#B4009E"); // BrightPurple maps to brightMagenta
        xtermTheme["brightCyan"].Should().Be("#61D6D6");
        xtermTheme["brightWhite"].Should().Be("#F2F2F2");
    }

    [Fact]
    public void ToXtermTheme_WithNullTheme_ThrowsArgumentNullException()
    {
        var action = () => ThemeAdapter.ToXtermTheme(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region CreateDefaultDarkXtermTheme Tests

    [Fact]
    public void CreateDefaultDarkXtermTheme_ReturnsValidTheme()
    {
        var theme = ThemeAdapter.CreateDefaultDarkXtermTheme();

        theme.Should().ContainKey("background");
        theme.Should().ContainKey("foreground");
        theme.Should().ContainKey("cursor");
        theme.Should().ContainKey("selectionBackground");
    }

    [Fact]
    public void CreateDefaultDarkXtermTheme_HasDarkBackground()
    {
        var theme = ThemeAdapter.CreateDefaultDarkXtermTheme();

        theme["background"].Should().Be("#0C0C0C");
    }

    [Fact]
    public void CreateDefaultDarkXtermTheme_HasLightForeground()
    {
        var theme = ThemeAdapter.CreateDefaultDarkXtermTheme();

        theme["foreground"].Should().Be("#CCCCCC");
    }

    [Fact]
    public void CreateDefaultDarkXtermTheme_HasAll16Colors()
    {
        var theme = ThemeAdapter.CreateDefaultDarkXtermTheme();

        // Standard colors
        theme.Should().ContainKey("black");
        theme.Should().ContainKey("red");
        theme.Should().ContainKey("green");
        theme.Should().ContainKey("yellow");
        theme.Should().ContainKey("blue");
        theme.Should().ContainKey("magenta");
        theme.Should().ContainKey("cyan");
        theme.Should().ContainKey("white");

        // Bright colors
        theme.Should().ContainKey("brightBlack");
        theme.Should().ContainKey("brightRed");
        theme.Should().ContainKey("brightGreen");
        theme.Should().ContainKey("brightYellow");
        theme.Should().ContainKey("brightBlue");
        theme.Should().ContainKey("brightMagenta");
        theme.Should().ContainKey("brightCyan");
        theme.Should().ContainKey("brightWhite");
    }

    #endregion

    #region ColorToHex Tests

    [Fact]
    public void ColorToHex_ConvertsWhite()
    {
        var color = Colors.White; // R=255, G=255, B=255

        var result = ThemeAdapter.ColorToHex(color);

        result.Should().Be("#FFFFFF");
    }

    [Fact]
    public void ColorToHex_ConvertsBlack()
    {
        var color = Colors.Black; // R=0, G=0, B=0

        var result = ThemeAdapter.ColorToHex(color);

        result.Should().Be("#000000");
    }

    [Fact]
    public void ColorToHex_ConvertsRed()
    {
        var color = Colors.Red; // R=255, G=0, B=0

        var result = ThemeAdapter.ColorToHex(color);

        result.Should().Be("#FF0000");
    }

    [Fact]
    public void ColorToHex_ConvertsGreen()
    {
        var color = Colors.Lime; // R=0, G=255, B=0

        var result = ThemeAdapter.ColorToHex(color);

        result.Should().Be("#00FF00");
    }

    [Fact]
    public void ColorToHex_ConvertsBlue()
    {
        var color = Colors.Blue; // R=0, G=0, B=255

        var result = ThemeAdapter.ColorToHex(color);

        result.Should().Be("#0000FF");
    }

    [Fact]
    public void ColorToHex_ConvertsMixedColor()
    {
        var color = Color.FromRgb(0x12, 0x34, 0x56);

        var result = ThemeAdapter.ColorToHex(color);

        result.Should().Be("#123456");
    }

    #endregion
}
