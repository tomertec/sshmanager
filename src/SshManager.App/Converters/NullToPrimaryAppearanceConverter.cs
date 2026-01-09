using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a null value to ControlAppearance.Primary, non-null to Secondary.
/// Used for the "All" groups chip to highlight when no group filter is selected.
/// </summary>
public class NullToPrimaryAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
