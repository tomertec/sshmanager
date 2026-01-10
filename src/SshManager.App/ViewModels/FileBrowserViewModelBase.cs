using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace SshManager.App.ViewModels;

/// <summary>
/// Abstract base class for file browser view models.
/// Provides common navigation, selection, and file operation logic.
/// </summary>
/// <typeparam name="TQuickAccess">The type for quick access items (DriveInfoViewModel for local, RemoteQuickAccess for remote).</typeparam>
public abstract partial class FileBrowserViewModelBase<TQuickAccess> : ObservableObject, IFileBrowserViewModel
    where TQuickAccess : class
{
    protected readonly ILogger _logger;
    protected readonly Stack<string> _navigationHistory = new();

    /// <summary>
    /// Current directory path.
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
    /// Breadcrumb segments for path navigation.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BreadcrumbSegment> _breadcrumbs = [];

    /// <summary>
    /// Quick access items (drives for local, locations for remote).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TQuickAccess> _quickAccess = [];

    /// <summary>
    /// Whether navigation back is available.
    /// </summary>
    public abstract bool CanGoBack { get; }

    /// <summary>
    /// Whether navigation up is available.
    /// </summary>
    public abstract bool CanGoUp { get; }

    /// <summary>
    /// Gets the name of this browser type for logging.
    /// </summary>
    protected abstract string BrowserTypeName { get; }

    protected FileBrowserViewModelBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes the browser with the default starting directory.
    /// </summary>
    public abstract Task InitializeAsync();

    /// <summary>
    /// Normalizes a path for this filesystem type.
    /// </summary>
    protected abstract string NormalizePath(string path);

    /// <summary>
    /// Gets the parent path of the given path.
    /// </summary>
    protected abstract string GetParentPath(string path);

    /// <summary>
    /// Combines a base path with a relative path.
    /// </summary>
    protected abstract string CombinePaths(string basePath, string relativePath);

    /// <summary>
    /// Checks if a directory exists at the given path.
    /// </summary>
    protected abstract Task<bool> DirectoryExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Loads the directory items from the given path.
    /// </summary>
    protected abstract Task<List<FileItemViewModel>> LoadDirectoryItemsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Checks if the given path is the root.
    /// </summary>
    protected abstract bool IsRootPath(string path);

    /// <summary>
    /// Updates the breadcrumb navigation segments.
    /// </summary>
    protected abstract void UpdateBreadcrumbs();

    /// <summary>
    /// Renames the specified item.
    /// </summary>
    public abstract Task<bool> RenameAsync(FileItemViewModel item, string newName, CancellationToken ct = default);

    /// <summary>
    /// Deletes the specified item.
    /// </summary>
    public abstract Task<bool> DeleteAsync(FileItemViewModel item, bool recursive = false, CancellationToken ct = default);

    /// <summary>
    /// Navigates to the specified directory path.
    /// </summary>
    [RelayCommand]
    public virtual async Task NavigateToAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        _logger.LogDebug("Navigating to {BrowserType} path: {Path}", BrowserTypeName, path);
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            path = NormalizePath(path);

            if (!await DirectoryExistsAsync(path))
            {
                ErrorMessage = $"Directory not found: {path}";
                _logger.LogWarning("{BrowserType} directory not found: {Path}", BrowserTypeName, path);
                return;
            }

            // Save current path to history if navigating to a new location
            if (!string.IsNullOrEmpty(CurrentPath) && CurrentPath != path)
            {
                _navigationHistory.Push(CurrentPath);
            }

            CurrentPath = path;
            UpdateBreadcrumbs();

            var items = await LoadDirectoryItemsAsync(path);
            Items = new ObservableCollection<FileItemViewModel>(items);

            _logger.LogDebug("Loaded {Count} items from {BrowserType} directory", items.Count, BrowserTypeName);

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoUp));
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "Access denied to this directory";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load directory: {ex.Message}";
            _logger.LogError(ex, "Failed to navigate to {BrowserType} path {Path}", BrowserTypeName, path);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens the selected item (navigates if directory).
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
    public virtual async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath) || IsRootPath(CurrentPath)) return;

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
            _logger.LogDebug("Refreshing {BrowserType} directory: {Path}", BrowserTypeName, CurrentPath);
            var path = CurrentPath;
            CurrentPath = ""; // Clear to avoid history push
            await NavigateToAsync(path);
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
    /// Copies the full path of the selected item to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyPath(FileItemViewModel? item)
    {
        if (item == null || item.IsParentDirectory) return;

        try
        {
            Clipboard.SetText(item.FullPath);
            _logger.LogDebug("Copied {BrowserType} path to clipboard: {Path}", BrowserTypeName, item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy path to clipboard");
        }
    }
}
