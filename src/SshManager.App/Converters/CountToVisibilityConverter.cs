using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a count to Visibility.
/// Default: Returns Visible when count > 0, Collapsed when count is 0.
/// With parameter "invert": Returns Visible when count is 0, Collapsed when count > 0.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0
        };

        var invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            // For empty states: Visible when count == 0
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Default: Visible when count > 0
        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
