using System.Globalization;
using System.Windows.Data;
using SshManager.Core.Models;

namespace SshManager.App.Converters;

/// <summary>
/// Converts enum values to human-readable descriptions.
/// </summary>
public class EnumToDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AutocompletionMode mode)
        {
            return mode switch
            {
                AutocompletionMode.RemoteShell => "Remote Shell",
                AutocompletionMode.LocalHistory => "Local History",
                AutocompletionMode.Hybrid => "Hybrid",
                _ => value.ToString() ?? string.Empty
            };
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
