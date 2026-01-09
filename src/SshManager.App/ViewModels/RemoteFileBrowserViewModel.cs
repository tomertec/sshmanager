using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for browsing a remote SFTP filesystem.
/// Provides navigation, file listing, and remote operations.
/// </summary>
public partial class RemoteFileBrowserViewModel : ObservableObject
{
    private readonly ILogger<RemoteFileBrowserViewModel> _logger;
    private readonly Stack<string> _navigationHistory = new();
    private ISftpSession? _session;

    /// <summary>
    /// Current remote directory path.
    /// </summary>
    [ObservableProperty]
    private string _currentPath = "";

    /// <summary>
    /// Items in the current directory.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _items = [];

    /// <summary>
    /// Currently selected item.
    /// </summary>
    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    /// <summary>
    /// Currently selected items (for multi-select).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _selectedItems = [];

    /// <summary>
    /// Whether the browser is currently loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Error message if loading failed.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Whether the SFTP session is connected.
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// The home directory on the remote server.
    /// </summary>
    [ObservableProperty]
    private string _homeDirectory = "";

    /// <summary>
    /// Whether navigation back is available.
    /// </summary>
    public bool CanGoBack => _navigationHistory.Count > 0;

    /// <summary>
    /// Whether navigation up is available.
    /// </summary>
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && CurrentPath != "/";

    /// <summary>
    /// Breadcrumb segments for path navigation.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BreadcrumbSegment> _breadcrumbs = [];

    /// <summary>
    /// Quick access locations on the remote server.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RemoteQuickAccess> _quickAccess = [];

    public RemoteFileBrowserViewModel(ILogger<RemoteFileBrowserViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<RemoteFileBrowserViewModel>.Instance;
    }

    /// <summary>
    /// Sets the SFTP session for remote operations.
    /// </summary>
    public void SetSession(ISftpSession session)
    {
        if (_session != null)
        {
            _session.Disconnected -= OnSessionDisconnected;
        }

        _session = session;
        IsConnected = session.IsConnected;
        HomeDirectory = session.WorkingDirectory;

        _session.Disconnected += OnSessionDisconnected;

        InitializeQuickAccess();
        _logger.LogDebug("SFTP session set, home directory: {HomeDir}", HomeDirectory);
    }

    /// <summary>
    /// Initializes the browser by navigating to the home directory.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_session == null)
        {
            ErrorMessage = "No SFTP session available";
            return;
        }

        await NavigateToAsync(HomeDirectory);
    }

    /// <summary>
    /// Navigates to the specified remote directory path.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || _session == null) return;

        _logger.LogDebug("Navigating to remote path: {Path}", path);
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Normalize the path
            path = NormalizePath(path);

            // Check if path exists
            var exists = await _session.ExistsAsync(path);
            if (!exists)
            {
                ErrorMessage = $"Directory not found: {path}";
                _logger.LogWarning("Remote directory not found: {Path}", path);
                return;
            }

            // Save current path to history if navigating to a new location
            if (!string.IsNullOrEmpty(CurrentPath) && CurrentPath != path)
            {
                _navigationHistory.Push(CurrentPath);
            }

            CurrentPath = path;
            UpdateBreadcrumbs();

            var items = new List<FileItemViewModel>();

            // Add parent directory if not at root
            if (path != "/")
            {
                var parentPath = GetParentPath(path);
                items.Add(FileItemViewModel.CreateParentDirectory(parentPath));
            }

            // Load directory contents
            var remoteItems = await _session.ListDirectoryAsync(path);

            // Filter out . and .. entries, sort directories first then by name
            var sortedItems = remoteItems
                .Where(i => i.Name != "." && i.Name != "..")
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name)
                .Select(FileItemViewModel.FromSftpFileItem);

            items.AddRange(sortedItems);

            Items = new ObservableCollection<FileItemViewModel>(items);
            _logger.LogDebug("Loaded {Count} items from remote directory", items.Count);

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoUp));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load directory: {ex.Message}";
            _logger.LogError(ex, "Failed to navigate to remote path {Path}", path);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens the selected item (navigates if directory, otherwise does nothing).
    /// </summary>
    [RelayCommand]
    public async Task OpenItemAsync(FileItemViewModel? item)
    {
        if (item == null) return;

        if (item.IsDirectory)
        {
            await NavigateToAsync(item.FullPath);
        }
    }

    /// <summary>
    /// Navigates back to the previous directory.
    /// </summary>
    [RelayCommand]
    public async Task GoBackAsync()
    {
        if (_navigationHistory.Count == 0) return;

        var previousPath = _navigationHistory.Pop();
        CurrentPath = ""; // Clear to avoid re-pushing to history
        await NavigateToAsync(previousPath);
        _navigationHistory.Pop(); // Remove the path that was just added
    }

    /// <summary>
    /// Navigates up to the parent directory.
    /// </summary>
    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || CurrentPath == "/") return;

        var parentPath = GetParentPath(CurrentPath);
        await NavigateToAsync(parentPath);
    }

    /// <summary>
    /// Refreshes the current directory listing.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _logger.LogDebug("Refreshing remote directory: {Path}", CurrentPath);
            var path = CurrentPath;
            CurrentPath = ""; // Clear to avoid history push
            await NavigateToAsync(path);
        }
    }

    /// <summary>
    /// Navigates to the home directory.
    /// </summary>
    [RelayCommand]
    public async Task GoHomeAsync()
    {
        if (!string.IsNullOrEmpty(HomeDirectory))
        {
            await NavigateToAsync(HomeDirectory);
        }
    }

    /// <summary>
    /// Navigates to a quick access location.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToQuickAccessAsync(RemoteQuickAccess? location)
    {
        if (location != null)
        {
            await NavigateToAsync(location.Path);
        }
    }

    /// <summary>
    /// Navigates to a breadcrumb segment.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToBreadcrumbAsync(BreadcrumbSegment? segment)
    {
        if (segment != null)
        {
            await NavigateToAsync(segment.FullPath);
        }
    }

    /// <summary>
    /// Creates a new directory in the current location.
    /// </summary>
    public async Task<bool> CreateDirectoryAsync(string name, CancellationToken ct = default)
    {
        if (_session == null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            var newPath = CombinePaths(CurrentPath, name);
            await _session.CreateDirectoryAsync(newPath, ct);
            _logger.LogInformation("Created remote directory: {Path}", newPath);
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create directory: {Name}", name);
            ErrorMessage = $"Failed to create directory: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Deletes the specified item.
    /// </summary>
    public async Task<bool> DeleteAsync(FileItemViewModel item, bool recursive = false, CancellationToken ct = default)
    {
        if (_session == null) return false;

        try
        {
            await _session.DeleteAsync(item.FullPath, recursive, ct);
            _logger.LogInformation("Deleted remote item: {Path}", item.FullPath);
            Items.Remove(item);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete: {Path}", item.FullPath);
            ErrorMessage = $"Failed to delete: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Renames the specified item.
    /// </summary>
    public async Task<bool> RenameAsync(FileItemViewModel item, string newName, CancellationToken ct = default)
    {
        if (_session == null || string.IsNullOrWhiteSpace(newName)) return false;

        try
        {
            var newPath = CombinePaths(GetParentPath(item.FullPath), newName);
            await _session.RenameAsync(item.FullPath, newPath, ct);
            _logger.LogInformation("Renamed remote item from {OldPath} to {NewPath}", item.FullPath, newPath);
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename: {Path}", item.FullPath);
            ErrorMessage = $"Failed to rename: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Copies the full path of the selected item to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyPath(FileItemViewModel? item)
    {
        if (item == null || item.IsParentDirectory) return;

        try
        {
            Clipboard.SetText(item.FullPath);
            _logger.LogDebug("Copied remote path to clipboard: {Path}", item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy path to clipboard");
        }
    }

    /// <summary>
    /// Gets the underlying SFTP session.
    /// </summary>
    public ISftpSession? GetSession() => _session;

    private void OnSessionDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        ErrorMessage = "SFTP session disconnected";
        _logger.LogWarning("SFTP session disconnected");
    }

    private void InitializeQuickAccess()
    {
        var locations = new List<RemoteQuickAccess>
        {
            new() { Name = "Home", Path = HomeDirectory, Icon = "Home" },
            new() { Name = "Root", Path = "/", Icon = "Folder" },
            new() { Name = "/tmp", Path = "/tmp", Icon = "Folder" },
            new() { Name = "/var/log", Path = "/var/log", Icon = "Folder" },
            new() { Name = "/etc", Path = "/etc", Icon = "Folder" }
        };

        QuickAccess = new ObservableCollection<RemoteQuickAccess>(locations);
    }

    private void UpdateBreadcrumbs()
    {
        var segments = new List<BreadcrumbSegment>();

        if (string.IsNullOrEmpty(CurrentPath)) return;

        // Add root
        segments.Add(new BreadcrumbSegment { Name = "/", FullPath = "/" });

        if (CurrentPath == "/")
        {
            Breadcrumbs = new ObservableCollection<BreadcrumbSegment>(segments);
            return;
        }

        // Add path segments
        var parts = CurrentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentFullPath = "";

        foreach (var part in parts)
        {
            currentFullPath = $"{currentFullPath}/{part}";
            segments.Add(new BreadcrumbSegment
            {
                Name = part,
                FullPath = currentFullPath
            });
        }

        Breadcrumbs = new ObservableCollection<BreadcrumbSegment>(segments);
    }

    private static string NormalizePath(string path)
    {
        // Ensure path starts with /
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        // Remove trailing slash (except for root)
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        // Normalize multiple slashes
        while (path.Contains("//"))
        {
            path = path.Replace("//", "/");
        }

        return path;
    }

    private static string GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";

        var normalized = NormalizePath(path);
        var lastSlash = normalized.LastIndexOf('/');

        if (lastSlash <= 0) return "/";

        return normalized[..lastSlash];
    }

    private static string CombinePaths(string basePath, string relativePath)
    {
        basePath = NormalizePath(basePath);
        if (basePath == "/")
        {
            return "/" + relativePath;
        }
        return basePath + "/" + relativePath;
    }
}

/// <summary>
/// Represents a quick access location on the remote server.
/// </summary>
public class RemoteQuickAccess
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Icon { get; init; } = "Folder";
}
