using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a hex color string (e.g., "#FF5733") to a SolidColorBrush.
/// Returns a transparent brush if the value is null or invalid.
/// </summary>
public class StringToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Invalid color format, return transparent
                return TransparentBrush;
            }
        }

        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            return brush.Color.ToString();
        }

        return string.Empty;
    }
}
