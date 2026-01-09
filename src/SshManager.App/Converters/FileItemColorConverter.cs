using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SshManager.App.ViewModels;

namespace SshManager.App.Converters;

/// <summary>
/// Converts FileItemViewModel to appropriate foreground color.
/// Folders get a distinctive golden color for easy identification.
/// </summary>
public class FileItemColorConverter : IValueConverter
{
    // Tan/yellowish color for folders - matches common file manager style
    private static readonly SolidColorBrush FolderBrush = new(Color.FromRgb(0xD4, 0xC4, 0x8C));

    // Cyan color for symbolic links
    private static readonly SolidColorBrush SymlinkBrush = new(Color.FromRgb(0x56, 0xB6, 0xC2));

    // White color for regular files
    private static readonly SolidColorBrush FileBrush = new(Color.FromRgb(0xF0, 0xF0, 0xF0));

    static FileItemColorConverter()
    {
        // Freeze brushes for performance
        FolderBrush.Freeze();
        SymlinkBrush.Freeze();
        FileBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FileItemViewModel item)
        {
            return FileBrush;
        }

        // Parent directory and folders use the same tan/yellow color
        if (item.IsParentDirectory || item.IsDirectory)
        {
            return FolderBrush;
        }

        if (item.IsSymbolicLink)
        {
            return SymlinkBrush;
        }

        return FileBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
