using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace SshManager.App.Converters;

/// <summary>
/// Converts file extension to WPF-UI SymbolRegular icon.
/// </summary>
public class FileIconConverter : IMultiValueConverter
{
    private static readonly Dictionary<string, SymbolRegular> ExtensionIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        // Text and documents
        { ".txt", SymbolRegular.DocumentText24 },
        { ".md", SymbolRegular.DocumentText24 },
        { ".doc", SymbolRegular.DocumentText24 },
        { ".docx", SymbolRegular.DocumentText24 },
        { ".pdf", SymbolRegular.DocumentPdf24 },
        { ".rtf", SymbolRegular.DocumentText24 },

        // Code files
        { ".cs", SymbolRegular.Code24 },
        { ".js", SymbolRegular.Code24 },
        { ".ts", SymbolRegular.Code24 },
        { ".py", SymbolRegular.Code24 },
        { ".java", SymbolRegular.Code24 },
        { ".cpp", SymbolRegular.Code24 },
        { ".c", SymbolRegular.Code24 },
        { ".h", SymbolRegular.Code24 },
        { ".go", SymbolRegular.Code24 },
        { ".rs", SymbolRegular.Code24 },
        { ".rb", SymbolRegular.Code24 },
        { ".php", SymbolRegular.Code24 },
        { ".swift", SymbolRegular.Code24 },
        { ".kt", SymbolRegular.Code24 },

        // Web files
        { ".html", SymbolRegular.Globe24 },
        { ".htm", SymbolRegular.Globe24 },
        { ".css", SymbolRegular.Code24 },
        { ".scss", SymbolRegular.Code24 },
        { ".less", SymbolRegular.Code24 },
        { ".vue", SymbolRegular.Code24 },
        { ".jsx", SymbolRegular.Code24 },
        { ".tsx", SymbolRegular.Code24 },

        // Config files
        { ".json", SymbolRegular.Braces24 },
        { ".xml", SymbolRegular.Braces24 },
        { ".yaml", SymbolRegular.Braces24 },
        { ".yml", SymbolRegular.Braces24 },
        { ".toml", SymbolRegular.Braces24 },
        { ".ini", SymbolRegular.Settings24 },
        { ".config", SymbolRegular.Settings24 },
        { ".conf", SymbolRegular.Settings24 },

        // Scripts
        { ".sh", SymbolRegular.WindowConsole20 },
        { ".bash", SymbolRegular.WindowConsole20 },
        { ".bat", SymbolRegular.WindowConsole20 },
        { ".cmd", SymbolRegular.WindowConsole20 },
        { ".ps1", SymbolRegular.WindowConsole20 },

        // Images
        { ".png", SymbolRegular.Image24 },
        { ".jpg", SymbolRegular.Image24 },
        { ".jpeg", SymbolRegular.Image24 },
        { ".gif", SymbolRegular.Image24 },
        { ".bmp", SymbolRegular.Image24 },
        { ".ico", SymbolRegular.Image24 },
        { ".svg", SymbolRegular.Image24 },
        { ".webp", SymbolRegular.Image24 },

        // Audio/Video
        { ".mp3", SymbolRegular.MusicNote124 },
        { ".wav", SymbolRegular.MusicNote124 },
        { ".flac", SymbolRegular.MusicNote124 },
        { ".mp4", SymbolRegular.Video24 },
        { ".avi", SymbolRegular.Video24 },
        { ".mkv", SymbolRegular.Video24 },
        { ".mov", SymbolRegular.Video24 },

        // Archives
        { ".zip", SymbolRegular.FolderZip24 },
        { ".rar", SymbolRegular.FolderZip24 },
        { ".7z", SymbolRegular.FolderZip24 },
        { ".tar", SymbolRegular.FolderZip24 },
        { ".gz", SymbolRegular.FolderZip24 },
        { ".bz2", SymbolRegular.FolderZip24 },
        { ".xz", SymbolRegular.FolderZip24 },

        // Executables
        { ".exe", SymbolRegular.Apps24 },
        { ".dll", SymbolRegular.Apps24 },
        { ".msi", SymbolRegular.Apps24 },

        // Database
        { ".db", SymbolRegular.Database24 },
        { ".sqlite", SymbolRegular.Database24 },
        { ".sql", SymbolRegular.Database24 },

        // Keys and certs
        { ".pem", SymbolRegular.Key24 },
        { ".key", SymbolRegular.Key24 },
        { ".crt", SymbolRegular.Certificate24 },
        { ".cer", SymbolRegular.Certificate24 },
        { ".pub", SymbolRegular.Key24 },

        // Log files
        { ".log", SymbolRegular.DocumentText24 }
    };

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return SymbolRegular.Document24;
        }

        var isDirectory = values[0] is bool dir && dir;
        var isParentDirectory = values[1] is bool parent && parent;
        var extension = values.Length > 2 ? values[2] as string : null;

        if (isParentDirectory)
        {
            return SymbolRegular.ArrowUp24;
        }

        if (isDirectory)
        {
            return SymbolRegular.Folder24;
        }

        if (!string.IsNullOrEmpty(extension) && ExtensionIcons.TryGetValue(extension, out var icon))
        {
            return icon;
        }

        return SymbolRegular.Document24;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Simple converter that takes a single FileItemViewModel and returns the appropriate icon.
/// </summary>
public class SimpleFileIconConverter : IValueConverter
{
    private static readonly FileIconConverter MultiConverter = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SshManager.App.ViewModels.FileItemViewModel item)
        {
            return SymbolRegular.Document24;
        }

        return MultiConverter.Convert(
            [item.IsDirectory, item.IsParentDirectory, item.Extension],
            targetType,
            parameter,
            culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
