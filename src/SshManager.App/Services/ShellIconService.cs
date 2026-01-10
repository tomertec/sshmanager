using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SshManager.App.Services;

/// <summary>
/// Service for extracting Windows shell icons for files and folders.
/// Uses caching to improve performance for repeated icon requests.
/// </summary>
public class ShellIconService
{
    private static readonly Lazy<ShellIconService> _instance = new(() => new ShellIconService());
    public static ShellIconService Instance => _instance.Value;

    // Cache for extension-based icons (used for remote files)
    private readonly ConcurrentDictionary<string, ImageSource> _extensionIconCache = new(StringComparer.OrdinalIgnoreCase);

    // Cache for special icons
    private ImageSource? _folderIcon;
    private ImageSource? _parentFolderIcon;
    private ImageSource? _defaultFileIcon;

    private ShellIconService() { }

    /// <summary>
    /// Gets the icon for a file based on its path (for local files) or extension (for remote files).
    /// </summary>
    /// <param name="path">Full file path or just a filename with extension</param>
    /// <param name="isDirectory">Whether this is a directory</param>
    /// <param name="isParentDirectory">Whether this is the parent directory (..) item</param>
    /// <param name="isLocal">Whether this is a local file (true) or remote file (false)</param>
    /// <returns>An ImageSource for the file's icon</returns>
    public ImageSource GetIcon(string path, bool isDirectory, bool isParentDirectory, bool isLocal)
    {
        if (isParentDirectory)
        {
            return GetParentFolderIcon();
        }

        if (isDirectory)
        {
            return GetFolderIcon();
        }

        // For files, get icon based on extension
        var extension = Path.GetExtension(path)?.ToLowerInvariant() ?? "";

        if (string.IsNullOrEmpty(extension))
        {
            return GetDefaultFileIcon();
        }

        // Check cache first
        if (_extensionIconCache.TryGetValue(extension, out var cachedIcon))
        {
            return cachedIcon;
        }

        // Extract icon using shell API
        var icon = ExtractIconForExtension(extension);
        _extensionIconCache.TryAdd(extension, icon);
        return icon;
    }

    /// <summary>
    /// Gets the standard folder icon.
    /// </summary>
    public ImageSource GetFolderIcon()
    {
        if (_folderIcon != null)
        {
            return _folderIcon;
        }

        // Get folder icon using a temp directory approach
        var tempPath = Path.GetTempPath();
        _folderIcon = ExtractIcon(tempPath, isDirectory: true);
        return _folderIcon;
    }

    /// <summary>
    /// Gets the parent folder (..) icon - uses folder icon with overlay concept.
    /// </summary>
    public ImageSource GetParentFolderIcon()
    {
        if (_parentFolderIcon != null)
        {
            return _parentFolderIcon;
        }

        // Use the same folder icon for parent directory
        _parentFolderIcon = GetFolderIcon();
        return _parentFolderIcon;
    }

    /// <summary>
    /// Gets the default file icon for files without recognized extensions.
    /// </summary>
    public ImageSource GetDefaultFileIcon()
    {
        if (_defaultFileIcon != null)
        {
            return _defaultFileIcon;
        }

        _defaultFileIcon = ExtractIconForExtension(".unknown");
        return _defaultFileIcon;
    }

    /// <summary>
    /// Extracts an icon for a given file extension.
    /// </summary>
    private ImageSource ExtractIconForExtension(string extension)
    {
        // Create a temporary fake path with the extension to get the associated icon
        var fakePath = $"file{extension}";
        return ExtractIcon(fakePath, isDirectory: false, useExtensionOnly: true);
    }

    /// <summary>
    /// Extracts an icon from a file or folder using the Windows Shell API.
    /// </summary>
    private ImageSource ExtractIcon(string path, bool isDirectory, bool useExtensionOnly = false)
    {
        try
        {
            var flags = SHGFI_ICON | SHGFI_SMALLICON;

            if (useExtensionOnly)
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            var fileAttributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            var shInfo = new SHFILEINFO();
            var result = SHGetFileInfo(path, fileAttributes, ref shInfo, (uint)Marshal.SizeOf(shInfo), flags);

            if (result != IntPtr.Zero && shInfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                        shInfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    imageSource.Freeze(); // Make it cross-thread accessible
                    return imageSource;
                }
                finally
                {
                    DestroyIcon(shInfo.hIcon);
                }
            }
        }
        catch
        {
            // Fall through to return default icon
        }

        // Return a default icon if extraction fails
        return CreateDefaultIcon();
    }

    /// <summary>
    /// Creates a default icon when shell extraction fails.
    /// </summary>
    private static ImageSource CreateDefaultIcon()
    {
        // Create a simple default icon as a drawing
        var drawingGroup = new DrawingGroup();
        using (var context = drawingGroup.Open())
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            brush.Freeze();
            context.DrawRectangle(brush, null, new Rect(0, 0, 16, 16));
        }

        var image = new DrawingImage(drawingGroup);
        image.Freeze();
        return image;
    }

    #region Win32 API

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    #endregion
}
