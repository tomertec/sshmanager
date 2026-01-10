using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a hex color string (e.g., "#FF5733") to a SolidColorBrush.
/// Returns a transparent brush if the value is null or invalid.
/// </summary>
public class GroupColorConverter : IValueConverter
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    static GroupColorConverter()
    {
        TransparentBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
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

/// <summary>
/// Converts a hex color string to a SolidColorBrush with adjustable opacity.
/// Pass the opacity value (0.0-1.0) as the converter parameter.
/// </summary>
public class GroupColorWithOpacityConverter : IValueConverter
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    static GroupColorWithOpacityConverter()
    {
        TransparentBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);

                // Apply opacity if parameter is provided
                if (parameter is string opacityStr && double.TryParse(opacityStr, out var opacity))
                {
                    color.A = (byte)(opacity * 255);
                }
                else if (parameter is double opacityDouble)
                {
                    color.A = (byte)(opacityDouble * 255);
                }

                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
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
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a Visibility value.
/// Returns Visible if the color is set, Collapsed if null/empty.
/// </summary>
public class GroupColorToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrWhiteSpace(hexColor))
        {
            return System.Windows.Visibility.Visible;
        }

        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
