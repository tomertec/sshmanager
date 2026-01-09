using System.Globalization;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a string to its first letter (uppercase).
/// Returns "?" if the string is null or empty.
/// </summary>
public class FirstLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            return name[0].ToString().ToUpper();
        }

        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
