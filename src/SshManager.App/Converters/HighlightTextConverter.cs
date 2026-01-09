using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace SshManager.App.Converters;

/// <summary>
/// Highlights characters in text that match a search query.
/// Used in Quick Connect overlay to show fuzzy match results.
/// </summary>
public class HighlightTextConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush HighlightBrush;

    static HighlightTextConverter()
    {
        HighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
        HighlightBrush.Freeze();
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string text || values[1] is not string search)
        {
            return new TextBlock { Text = values[0]?.ToString() ?? string.Empty };
        }

        var textBlock = new TextBlock();

        if (string.IsNullOrWhiteSpace(search))
        {
            textBlock.Inlines.Add(new Run(text));
            return textBlock;
        }

        int lastIndex = 0;
        string lowerText = text.ToLowerInvariant();
        string lowerSearch = search.ToLowerInvariant();

        // Highlight each character that matches the search query in order
        foreach (char c in lowerSearch)
        {
            int index = lowerText.IndexOf(c, lastIndex);
            if (index >= 0)
            {
                // Add non-highlighted text before the match
                if (index > lastIndex)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(lastIndex, index - lastIndex)));
                }

                // Add highlighted match
                textBlock.Inlines.Add(new Run(text.Substring(index, 1))
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = HighlightBrush
                });

                lastIndex = index + 1;
            }
        }

        // Add remaining text
        if (lastIndex < text.Length)
        {
            textBlock.Inlines.Add(new Run(text.Substring(lastIndex)));
        }

        return textBlock;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
