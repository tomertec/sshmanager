using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a boolean to ControlAppearance.Primary when true, Secondary when false.
/// Used for segmented button styling.
/// </summary>
public class BoolToPrimaryAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
        return ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ControlAppearance appearance)
        {
            return appearance == ControlAppearance.Primary;
        }
        return false;
    }
}
