using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a boolean collapse state to GridLength.
/// Returns 0 width if collapsed, Star(1) if expanded.
/// </summary>
public class CollapsedWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isCollapsed)
        {
            return isCollapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
        return new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength length)
        {
            return length.Value == 0;
        }
        return false;
    }
}
