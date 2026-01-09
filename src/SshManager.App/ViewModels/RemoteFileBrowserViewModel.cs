using System.Collections.ObjectModel;
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
public partial class RemoteFileBrowserViewModel : FileBrowserViewModelBase<RemoteQuickAccess>, IDisposable
{
    private ISftpSession? _session;
    private bool _disposed;
    private string _hostname = "";
    private Func<string>? _getFavoritesCallback;
    private Action<string>? _saveFavoritesCallback;

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
    /// Favorite/bookmarked paths for this host.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RemoteQuickAccess> _favorites = [];

    /// <summary>
    /// Whether the current path is bookmarked.
    /// </summary>
    public bool IsCurrentPathFavorite => !string.IsNullOrEmpty(CurrentPath) &&
        Favorites.Any(f => f.Path == CurrentPath);

    /// <inheritdoc />
    public override bool CanGoBack => _navigationHistory.Count > 0;

    /// <inheritdoc />
    public override bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && CurrentPath != "/";

    /// <inheritdoc />
    protected override string BrowserTypeName => "remote";

    public RemoteFileBrowserViewModel(ILogger<RemoteFileBrowserViewModel>? logger = null)
        : base(logger ?? NullLogger<RemoteFileBrowserViewModel>.Instance)
    {
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
    /// Sets the hostname and favorites callbacks for bookmark management.
    /// </summary>
    public void SetFavoritesSupport(string hostname, Func<string> getFavorites, Action<string> saveFavorites)
    {
        _hostname = hostname;
        _getFavoritesCallback = getFavorites;
        _saveFavoritesCallback = saveFavorites;
        LoadFavorites();
    }

    /// <summary>
    /// Loads favorites from storage.
    /// </summary>
    private void LoadFavorites()
    {
        if (_getFavoritesCallback == null || string.IsNullOrEmpty(_hostname))
        {
            return;
        }

        try
        {
            var allFavorites = _getFavoritesCallback();
            var hostPrefix = $"{_hostname}:";

            var hostFavorites = allFavorites
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(f => f.StartsWith(hostPrefix))
                .Select(f => f[hostPrefix.Length..])
                .Select(path => new RemoteQuickAccess
                {
                    Name = GetFavoriteDisplayName(path),
                    Path = path,
                    Icon = "Star"
                })
                .ToList();

            Favorites = new ObservableCollection<RemoteQuickAccess>(hostFavorites);
            _logger.LogDebug("Loaded {Count} favorites for {Host}", hostFavorites.Count, _hostname);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load favorites");
        }
    }

    /// <summary>
    /// Saves favorites to storage.
    /// </summary>
    private void SaveFavorites()
    {
        if (_saveFavoritesCallback == null || string.IsNullOrEmpty(_hostname))
        {
            return;
        }

        try
        {
            // Get existing favorites for other hosts
            var allFavorites = _getFavoritesCallback?.Invoke() ?? "";
            var hostPrefix = $"{_hostname}:";

            var otherHostFavorites = allFavorites
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(f => !f.StartsWith(hostPrefix))
                .ToList();

            // Add this host's favorites
            var thisHostFavorites = Favorites.Select(f => $"{hostPrefix}{f.Path}");

            var combined = otherHostFavorites.Concat(thisHostFavorites);
            var newValue = string.Join("|", combined);

            _saveFavoritesCallback(newValue);
            _logger.LogDebug("Saved favorites for {Host}", _hostname);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save favorites");
        }
    }

    /// <summary>
    /// Gets a display name for a favorite path.
    /// </summary>
    private static string GetFavoriteDisplayName(string path)
    {
        if (path == "/") return "/";
        var name = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>
    /// Toggles the current path as a favorite.
    /// </summary>
    [RelayCommand]
    public void ToggleFavorite()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        var existing = Favorites.FirstOrDefault(f => f.Path == CurrentPath);
        if (existing != null)
        {
            Favorites.Remove(existing);
            _logger.LogInformation("Removed favorite: {Path}", CurrentPath);
        }
        else
        {
            Favorites.Add(new RemoteQuickAccess
            {
                Name = GetFavoriteDisplayName(CurrentPath),
                Path = CurrentPath,
                Icon = "Star"
            });
            _logger.LogInformation("Added favorite: {Path}", CurrentPath);
        }

        SaveFavorites();
        OnPropertyChanged(nameof(IsCurrentPathFavorite));
    }

    /// <summary>
    /// Removes a specific favorite.
    /// </summary>
    [RelayCommand]
    public void RemoveFavorite(RemoteQuickAccess? favorite)
    {
        if (favorite == null) return;

        Favorites.Remove(favorite);
        SaveFavorites();
        OnPropertyChanged(nameof(IsCurrentPathFavorite));
    }

    /// <summary>
    /// Navigates to a favorite path.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToFavoriteAsync(RemoteQuickAccess? favorite)
    {
        if (favorite != null)
        {
            await NavigateToAsync(favorite.Path);
        }
    }

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        if (_session == null)
        {
            ErrorMessage = "No SFTP session available";
            return;
        }

        await NavigateToAsync(HomeDirectory);
    }

    /// <inheritdoc />
    public override async Task NavigateToAsync(string path)
    {
        if (_session == null)
        {
            ErrorMessage = "No SFTP session available";
            return;
        }

        await base.NavigateToAsync(path);
        OnPropertyChanged(nameof(IsCurrentPathFavorite));
    }

    /// <inheritdoc />
    protected override string NormalizePath(string path)
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

    /// <inheritdoc />
    protected override string GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";

        var normalized = NormalizePath(path);
        var lastSlash = normalized.LastIndexOf('/');

        if (lastSlash <= 0) return "/";

        return normalized[..lastSlash];
    }

    /// <inheritdoc />
    protected override string CombinePaths(string basePath, string relativePath)
    {
        basePath = NormalizePath(basePath);
        if (basePath == "/")
        {
            return "/" + relativePath;
        }
        return basePath + "/" + relativePath;
    }

    /// <inheritdoc />
    protected override async Task<bool> DirectoryExistsAsync(string path, CancellationToken ct = default)
    {
        if (_session == null) return false;
        return await _session.ExistsAsync(path, ct);
    }

    /// <inheritdoc />
    protected override bool IsRootPath(string path) => path == "/";

    /// <inheritdoc />
    protected override async Task<List<FileItemViewModel>> LoadDirectoryItemsAsync(string path, CancellationToken ct = default)
    {
        var items = new List<FileItemViewModel>();

        if (_session == null) return items;

        // Add parent directory if not at root
        if (path != "/")
        {
            var parentPath = GetParentPath(path);
            items.Add(FileItemViewModel.CreateParentDirectory(parentPath));
        }

        // Load directory contents
        var remoteItems = await _session.ListDirectoryAsync(path, ct);

        // Filter out . and .. entries, sort directories first then by name
        var sortedItems = remoteItems
            .Where(i => i.Name != "." && i.Name != "..")
            .OrderByDescending(i => i.IsDirectory)
            .ThenBy(i => i.Name)
            .Select(FileItemViewModel.FromSftpFileItem);

        items.AddRange(sortedItems);

        return items;
    }

    /// <inheritdoc />
    protected override void UpdateBreadcrumbs()
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

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(FileItemViewModel item, bool recursive = false, CancellationToken ct = default)
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

    /// <inheritdoc />
    public override async Task<bool> RenameAsync(FileItemViewModel item, string newName, CancellationToken ct = default)
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

    public void Dispose()
    {
        if (_disposed) return;

        if (_session != null)
        {
            _session.Disconnected -= OnSessionDisconnected;
        }

        _disposed = true;
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
