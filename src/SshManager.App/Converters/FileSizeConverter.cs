using System.Globalization;
using System.Windows.Data;

namespace SshManager.App.Converters;

/// <summary>
/// Converts byte size to human-readable format (KB, MB, GB, etc.).
/// </summary>
public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            return "";
        }

        if (bytes == 0)
        {
            return "";
        }

        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0
            ? $"{size:N0} {suffixes[suffixIndex]}"
            : $"{size:N1} {suffixes[suffixIndex]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
