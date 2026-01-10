using System.Globalization;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts null to bool. Non-null values return true, null returns false.
/// Useful for enabling/disabling controls based on whether a value is selected.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
