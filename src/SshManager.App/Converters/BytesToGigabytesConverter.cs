using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace SshManager.App.Converters;

/// <summary>
/// Converts bytes to gigabytes as a double value.
/// Can be used as a markup extension for inline usage.
/// </summary>
public class BytesToGigabytesConverter : MarkupExtension, IValueConverter
{
    private static BytesToGigabytesConverter? _instance;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return bytes / (1024.0 * 1024.0 * 1024.0);
        }

        if (value is double d)
        {
            return d / (1024.0 * 1024.0 * 1024.0);
        }

        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _instance ??= new BytesToGigabytesConverter();
    }
}
