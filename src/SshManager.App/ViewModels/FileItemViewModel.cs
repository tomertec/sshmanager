using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel representing a single file or directory item in a file browser.
/// Used by both local and remote file browsers for consistent UI binding.
/// </summary>
public partial class FileItemViewModel : ObservableObject
{
    /// <summary>
    /// The file or directory name (without path).
    /// </summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>
    /// The full path to the file or directory.
    /// </summary>
    [ObservableProperty]
    private string _fullPath = "";

    /// <summary>
    /// File size in bytes (0 for directories).
    /// </summary>
    [ObservableProperty]
    private long _size;

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset _modifiedDate;

    /// <summary>
    /// True if this item is a directory.
    /// </summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>
    /// True if this item is selected in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// True if this is a symbolic link.
    /// </summary>
    [ObservableProperty]
    private bool _isSymbolicLink;

    /// <summary>
    /// Unix permissions for remote items (null for local items).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionsDisplay))]
    [NotifyPropertyChangedFor(nameof(PermissionsOctal))]
    private int? _permissions;

    /// <summary>
    /// True if this item represents going up to the parent directory.
    /// </summary>
    [ObservableProperty]
    private bool _isParentDirectory;

    /// <summary>
    /// Owner username on the remote system (null for local items).
    /// </summary>
    [ObservableProperty]
    private string? _owner;

    /// <summary>
    /// Group name on the remote system (null for local items).
    /// </summary>
    [ObservableProperty]
    private string? _group;

    /// <summary>
    /// File extension (for icon mapping).
    /// </summary>
    public string Extension => IsDirectory ? "" : Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>
    /// File extensions that can be opened in the text editor.
    /// </summary>
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Text files
        ".txt", ".md", ".log", ".env",
        // Config files
        ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".toml",
        // Scripts
        ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd",
        // Web files
        ".html", ".htm", ".css", ".scss", ".js", ".ts", ".jsx", ".tsx",
        // Programming languages
        ".cs", ".py", ".rb", ".php", ".java", ".go", ".rs", ".c", ".cpp", ".h", ".hpp",
        // Other
        ".sql", ".csv"
    };

    /// <summary>
    /// Extensionless file names that can be opened in the text editor.
    /// </summary>
    private static readonly HashSet<string> EditableFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Docker
        "Dockerfile", "docker-compose", ".dockerignore",
        // Build files
        "Makefile", "CMakeLists.txt", "Rakefile", "Gemfile", "Vagrantfile",
        // Git
        ".gitignore", ".gitattributes", ".gitmodules",
        // Editor configs
        ".editorconfig", ".prettierrc", ".eslintrc", ".stylelintrc",
        // Shell profiles
        ".bashrc", ".bash_profile", ".bash_aliases", ".profile", ".zshrc", ".zprofile", ".zshenv",
        ".vimrc", ".tmux.conf", ".screenrc",
        // Other configs
        ".htaccess", ".npmrc", ".yarnrc", ".nvmrc", ".ruby-version", ".python-version",
        "LICENSE", "CHANGELOG", "AUTHORS", "CONTRIBUTORS", "CODEOWNERS",
        "requirements.txt", "Procfile", "Brewfile"
    };

    /// <summary>
    /// True if this file can be opened in the text editor (based on extension or filename).
    /// </summary>
    public bool IsEditable => !IsDirectory && !IsParentDirectory &&
        (EditableExtensions.Contains(Extension) || EditableFileNames.Contains(Name));

    /// <summary>
    /// Display text for the file size.
    /// </summary>
    public string SizeDisplay => IsDirectory ? "" : FormatFileSize(Size);

    /// <summary>
    /// Display text for the modified date.
    /// </summary>
    public string ModifiedDisplay => ModifiedDate.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// Display text for permissions (rwxr-xr-x).
    /// </summary>
    public string PermissionsDisplay => Permissions.HasValue ? FormatPermissions(Permissions.Value) : "";

    /// <summary>
    /// Display text for permissions in octal (e.g. 0755).
    /// </summary>
    public string PermissionsOctal => Permissions.HasValue ? FormatPermissionsOctal(Permissions.Value) : "";

    /// <summary>
    /// Creates a FileItemViewModel from a local FileSystemInfo.
    /// </summary>
    public static FileItemViewModel FromFileSystemInfo(FileSystemInfo info)
    {
        var isDirectory = info is DirectoryInfo;
        long size = 0;
        if (info is FileInfo fileInfo)
        {
            size = fileInfo.Length;
        }

        return new FileItemViewModel
        {
            Name = info.Name,
            FullPath = info.FullName,
            Size = size,
            ModifiedDate = new DateTimeOffset(info.LastWriteTime),
            IsDirectory = isDirectory,
            IsSymbolicLink = info.LinkTarget != null
        };
    }

    /// <summary>
    /// Creates a FileItemViewModel from an SftpFileItem.
    /// </summary>
    public static FileItemViewModel FromSftpFileItem(SftpFileItem item)
    {
        return new FileItemViewModel
        {
            Name = item.Name,
            FullPath = item.FullPath,
            Size = item.Size,
            ModifiedDate = item.ModifiedDate,
            IsDirectory = item.IsDirectory,
            IsSymbolicLink = item.IsSymbolicLink,
            Permissions = item.Permissions,
            Owner = item.Owner,
            Group = item.Group
        };
    }

    /// <summary>
    /// Creates a parent directory item for navigation.
    /// </summary>
    public static FileItemViewModel CreateParentDirectory(string parentPath)
    {
        return new FileItemViewModel
        {
            Name = "..",
            FullPath = parentPath,
            IsDirectory = true,
            IsParentDirectory = true,
            ModifiedDate = DateTimeOffset.MinValue
        };
    }

    private static string FormatFileSize(long bytes)
    {
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

    private static string FormatPermissions(int permissions)
    {
        Span<char> chars = stackalloc char[9];
        chars[0] = (permissions & 0x100) != 0 ? 'r' : '-';
        chars[1] = (permissions & 0x080) != 0 ? 'w' : '-';
        chars[2] = (permissions & 0x040) != 0 ? 'x' : '-';
        chars[3] = (permissions & 0x020) != 0 ? 'r' : '-';
        chars[4] = (permissions & 0x010) != 0 ? 'w' : '-';
        chars[5] = (permissions & 0x008) != 0 ? 'x' : '-';
        chars[6] = (permissions & 0x004) != 0 ? 'r' : '-';
        chars[7] = (permissions & 0x002) != 0 ? 'w' : '-';
        chars[8] = (permissions & 0x001) != 0 ? 'x' : '-';
        return new string(chars);
    }

    private static string FormatPermissionsOctal(int permissions)
    {
        var owner = (permissions >> 6) & 0x7;
        var group = (permissions >> 3) & 0x7;
        var other = permissions & 0x7;
        return $"0{owner}{group}{other}";
    }
}
