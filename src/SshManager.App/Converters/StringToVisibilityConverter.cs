using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a string to Visibility. Non-empty strings are Visible, empty/null are Collapsed.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
