using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SshManager.App.Services;
using SshManager.App.ViewModels;

namespace SshManager.App.Converters;

/// <summary>
/// Converts a FileItemViewModel to its Windows shell icon as an ImageSource.
/// This displays real file icons as they appear in Windows Explorer.
/// </summary>
public class ShellFileIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FileItemViewModel item)
        {
            return ShellIconService.Instance.GetDefaultFileIcon();
        }

        // Determine if this is a local file (no permissions = local)
        var isLocal = !item.Permissions.HasValue;

        return ShellIconService.Instance.GetIcon(
            item.FullPath,
            item.IsDirectory,
            item.IsParentDirectory,
            isLocal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
