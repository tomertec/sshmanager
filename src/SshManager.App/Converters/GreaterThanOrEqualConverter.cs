using System.Globalization;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a numeric value to true if it's greater than or equal to the parameter.
/// Used for step indicator highlighting in wizards.
/// </summary>
public class GreaterThanOrEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out var paramValue))
        {
            return intValue >= paramValue;
        }

        if (value is int intValue2 && parameter is int paramValue2)
        {
            return intValue2 >= paramValue2;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
