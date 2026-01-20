using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a hex color string (e.g., "#FF5733") to a Color.
/// Returns transparent color if the value is null or invalid.
/// </summary>
public class ColorStringConverter : IValueConverter
{
    private static readonly Color TransparentColor = Colors.Transparent;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hexColor);
            }
            catch
            {
                // Invalid color format, return transparent
                return TransparentColor;
            }
        }

        return TransparentColor;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return color.ToString();
        }

        return string.Empty;
    }
}
