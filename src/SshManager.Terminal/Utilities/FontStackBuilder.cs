namespace SshManager.Terminal.Utilities;

/// <summary>
/// Static utility class for building CSS font stacks for terminal rendering.
/// </summary>
public static class FontStackBuilder
{
    /// <summary>
    /// Default font fallbacks for terminal rendering.
    /// Includes popular monospace fonts in order of preference.
    /// </summary>
    private static readonly string[] DefaultFallbacks =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Source Code Pro",
        "Source Code Pro Powerline",
        "Fira Code",
        "JetBrains Mono",
        "Courier New",
        "monospace"
    ];

    /// <summary>
    /// Builds a CSS font stack string from a preferred font and fallbacks.
    /// </summary>
    /// <param name="preferredFont">The preferred font to use.</param>
    /// <param name="fallbacks">Optional custom fallback fonts. If null, uses default fallbacks.</param>
    /// <returns>A CSS-compatible font stack string.</returns>
    public static string Build(string preferredFont, string[]? fallbacks = null)
    {
        var fontFallbacks = fallbacks ?? DefaultFallbacks;
        var fonts = new List<string>(fontFallbacks.Length + 1)
        {
            QuoteIfNeeded(preferredFont)
        };

        foreach (var fallback in fontFallbacks)
        {
            // Skip if already in the list (case-insensitive comparison)
            var quotedFallback = QuoteIfNeeded(fallback);
            if (fonts.Any(f => f.Equals(quotedFallback, StringComparison.OrdinalIgnoreCase) ||
                               f.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            fonts.Add(quotedFallback);
        }

        return string.Join(", ", fonts);
    }

    /// <summary>
    /// Quotes a font name if it contains spaces or special characters.
    /// </summary>
    /// <param name="font">The font name to potentially quote.</param>
    /// <returns>The font name, quoted if necessary.</returns>
    internal static string QuoteIfNeeded(string font)
    {
        var trimmed = font.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        // Already quoted
        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
            (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            return trimmed;
        }

        // Needs quoting if contains spaces or commas
        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Contains(','))
        {
            // Escape any existing quotes and wrap in double quotes
            return $"\"{trimmed.Replace("\"", "\\\"")}\"";
        }

        return trimmed;
    }

    /// <summary>
    /// Gets the default font fallback list.
    /// </summary>
    public static IReadOnlyList<string> GetDefaultFallbacks() => DefaultFallbacks;
}
