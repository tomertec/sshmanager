using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts null to Visibility. Null values are Visible, non-null are Collapsed.
/// Useful for showing placeholder content when a value is not set.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
