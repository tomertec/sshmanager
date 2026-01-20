using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a value to Visibility by comparing it with the converter parameter.
/// Returns Visible if the value equals the parameter, Collapsed otherwise.
/// Useful for showing UI elements based on enum values.
/// </summary>
public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null)
            return Visibility.Visible;

        if (value == null || parameter == null)
            return Visibility.Collapsed;

        // Handle enum comparison
        if (value.GetType().IsEnum && parameter.GetType().IsEnum)
        {
            return value.Equals(parameter) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Handle string comparison
        return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
