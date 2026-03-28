using FluentAssertions;
using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Tests.Utilities;

/// <summary>
/// Unit tests for FontStackBuilder static utility class.
/// Tests font quoting and fallback stacking.
/// </summary>
/// <remarks>
/// Note: The QuoteIfNeeded method is internal. Quoting behavior is tested indirectly
/// through the public Build method.
/// </remarks>
public class FontStackBuilderTests
{
    #region Build Tests

    [Fact]
    public void Build_PreferredFont_IsFirstInStack()
    {
        // Arrange
        var preferredFont = "Fira Code";

        // Act
        var result = FontStackBuilder.Build(preferredFont);

        // Assert
        result.Should().StartWith("\"Fira Code\"");
    }

    [Fact]
    public void Build_WithDefaultFallbacks_ContainsFallbacks()
    {
        // Act
        var result = FontStackBuilder.Build("MyFont");

        // Assert
        result.Should().Contain("Consolas");
        result.Should().Contain("monospace");
        result.Should().Contain("\"Cascadia Mono\"");
    }

    [Fact]
    public void Build_PreferredFontSameAsDefaultFallback_NoDuplicates()
    {
        // Act
        var result = FontStackBuilder.Build("Consolas");

        // Assert
        // Count occurrences of "Consolas" - should be exactly 1
        var count = result.Split(',').Count(f => f.Trim().Equals("Consolas", StringComparison.OrdinalIgnoreCase));
        count.Should().Be(1);
    }

    [Fact]
    public void Build_WithCustomFallbacks_UsesCustomFallbacks()
    {
        // Arrange
        var preferredFont = "MyFont";
        var customFallbacks = new[] { "CustomFont1", "CustomFont2", "monospace" };

        // Act
        var result = FontStackBuilder.Build(preferredFont, customFallbacks);

        // Assert
        result.Should().Be("MyFont, CustomFont1, CustomFont2, monospace");
    }

    [Fact]
    public void Build_CustomFallbacksWithSpaces_QuotesCorrectly()
    {
        // Arrange
        var preferredFont = "MyFont";
        var customFallbacks = new[] { "Custom Font One", "monospace" };

        // Act
        var result = FontStackBuilder.Build(preferredFont, customFallbacks);

        // Assert
        result.Should().Be("MyFont, \"Custom Font One\", monospace");
    }

    [Fact]
    public void Build_EmptyCustomFallbacks_OnlyPreferredFont()
    {
        // Arrange
        var preferredFont = "MyFont";
        var customFallbacks = Array.Empty<string>();

        // Act
        var result = FontStackBuilder.Build(preferredFont, customFallbacks);

        // Assert
        result.Should().Be("MyFont");
    }

    [Fact]
    public void Build_PreferredFontWithSpaces_QuotesPreferred()
    {
        // Act
        var result = FontStackBuilder.Build("My Custom Font", new[] { "monospace" });

        // Assert
        result.Should().StartWith("\"My Custom Font\"");
    }

    [Fact]
    public void Build_SimpleFontWithoutSpaces_NoQuotes()
    {
        // Act
        var result = FontStackBuilder.Build("Consolas", new[] { "monospace" });

        // Assert
        result.Should().Be("Consolas, monospace");
    }

    [Fact]
    public void Build_FontWithComma_GetsQuoted()
    {
        // Act
        var result = FontStackBuilder.Build("Font,Name", new[] { "monospace" });

        // Assert
        result.Should().StartWith("\"Font,Name\"");
    }

    #endregion

    #region GetDefaultFallbacks Tests

    [Fact]
    public void GetDefaultFallbacks_ReturnsNonEmptyList()
    {
        // Act
        var fallbacks = FontStackBuilder.GetDefaultFallbacks();

        // Assert
        fallbacks.Should().NotBeEmpty();
    }

    [Fact]
    public void GetDefaultFallbacks_ContainsMonospace()
    {
        // Act
        var fallbacks = FontStackBuilder.GetDefaultFallbacks();

        // Assert
        fallbacks.Should().Contain("monospace");
    }

    [Fact]
    public void GetDefaultFallbacks_ContainsCommonTerminalFonts()
    {
        // Act
        var fallbacks = FontStackBuilder.GetDefaultFallbacks();

        // Assert
        fallbacks.Should().Contain("Consolas");
        fallbacks.Should().Contain("Cascadia Mono");
    }

    [Fact]
    public void GetDefaultFallbacks_IsReadOnly()
    {
        // Act
        var fallbacks = FontStackBuilder.GetDefaultFallbacks();

        // Assert - Should be IReadOnlyList
        fallbacks.Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    #endregion

    #region Full Font Stack Generation Tests

    [Fact]
    public void Build_GeneratesValidCssFontStack()
    {
        // Act
        var fontStack = FontStackBuilder.Build("JetBrains Mono");

        // Assert
        fontStack.Should().NotBeEmpty();
        fontStack.Should().Contain(","); // Should have multiple fonts
        fontStack.Should().EndWith("monospace"); // Should end with generic monospace
    }

    [Fact]
    public void Build_AllDefaultFallbacksIncluded()
    {
        // Arrange
        var defaultFallbacks = FontStackBuilder.GetDefaultFallbacks();

        // Act
        var fontStack = FontStackBuilder.Build("MyUniqueFont");

        // Assert - All default fallbacks should be in the result
        foreach (var fallback in defaultFallbacks)
        {
            // Account for quoting of fonts with spaces
            var expectedInStack = fallback.Contains(' ')
                ? $"\"{fallback}\""
                : fallback;
            fontStack.Should().Contain(expectedInStack,
                $"font stack should contain fallback: {fallback}");
        }
    }

    #endregion
}
