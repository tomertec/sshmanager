using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for browsing the local filesystem.
/// Provides navigation, file listing, and drive/folder quick access.
/// </summary>
public partial class LocalFileBrowserViewModel : ObservableObject
{
    private readonly ILogger<LocalFileBrowserViewModel> _logger;
    private readonly Stack<string> _navigationHistory = new();

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
    /// Available drives for quick access.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveInfoViewModel> _drives = [];

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
    /// Whether navigation back is available.
    /// </summary>
    public bool CanGoBack => _navigationHistory.Count > 0;

    /// <summary>
    /// Whether navigation up is available.
    /// </summary>
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) != null;

    /// <summary>
    /// Breadcrumb segments for path navigation.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BreadcrumbSegment> _breadcrumbs = [];

    public LocalFileBrowserViewModel(ILogger<LocalFileBrowserViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<LocalFileBrowserViewModel>.Instance;
        LoadDrives();
    }

    /// <summary>
    /// Initializes the browser with the user's home directory.
    /// </summary>
    public async Task InitializeAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await NavigateToAsync(userProfile);
    }

    /// <summary>
    /// Navigates to the specified directory path.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        _logger.LogDebug("Navigating to local path: {Path}", path);
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Normalize the path
            path = Path.GetFullPath(path);

            if (!Directory.Exists(path))
            {
                ErrorMessage = $"Directory not found: {path}";
                _logger.LogWarning("Directory not found: {Path}", path);
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
            var parentDir = Directory.GetParent(path);
            if (parentDir != null)
            {
                items.Add(FileItemViewModel.CreateParentDirectory(parentDir.FullName));
            }

            // Load directories first, then files (sorted alphabetically)
            await Task.Run(() =>
            {
                try
                {
                    var dirInfo = new DirectoryInfo(path);

                    // Add directories
                    foreach (var dir in dirInfo.EnumerateDirectories().OrderBy(d => d.Name))
                    {
                        try
                        {
                            items.Add(FileItemViewModel.FromFileSystemInfo(dir));
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip inaccessible directories
                        }
                    }

                    // Add files
                    foreach (var file in dirInfo.EnumerateFiles().OrderBy(f => f.Name))
                    {
                        try
                        {
                            items.Add(FileItemViewModel.FromFileSystemInfo(file));
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip inaccessible files
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Access denied to directory: {Path}", path);
                    throw;
                }
            });

            Items = new ObservableCollection<FileItemViewModel>(items);
            _logger.LogDebug("Loaded {Count} items from local directory", items.Count);

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
            _logger.LogError(ex, "Failed to navigate to {Path}", path);
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
        if (string.IsNullOrEmpty(CurrentPath)) return;

        var parent = Directory.GetParent(CurrentPath);
        if (parent != null)
        {
            await NavigateToAsync(parent.FullName);
        }
    }

    /// <summary>
    /// Refreshes the current directory listing.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _logger.LogDebug("Refreshing local directory: {Path}", CurrentPath);
            var path = CurrentPath;
            CurrentPath = ""; // Clear to avoid history push
            await NavigateToAsync(path);
        }
    }

    /// <summary>
    /// Navigates to a drive.
    /// </summary>
    [RelayCommand]
    public async Task NavigateToDriveAsync(DriveInfoViewModel? drive)
    {
        if (drive != null)
        {
            await NavigateToAsync(drive.RootPath);
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
            _logger.LogDebug("Copied path to clipboard: {Path}", item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy path to clipboard");
        }
    }

    /// <summary>
    /// Renames the specified item.
    /// </summary>
    public async Task<bool> RenameAsync(FileItemViewModel item, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        if (item.IsParentDirectory) return false;

        try
        {
            var parentDir = Path.GetDirectoryName(item.FullPath);
            if (string.IsNullOrEmpty(parentDir)) return false;

            var newPath = Path.Combine(parentDir, newName);

            if (item.IsDirectory)
            {
                await Task.Run(() => Directory.Move(item.FullPath, newPath), ct);
            }
            else
            {
                await Task.Run(() => File.Move(item.FullPath, newPath), ct);
            }

            _logger.LogInformation("Renamed local item from {OldPath} to {NewPath}", item.FullPath, newPath);
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
    /// Deletes the specified item.
    /// </summary>
    public async Task<bool> DeleteAsync(FileItemViewModel item, bool recursive = false, CancellationToken ct = default)
    {
        if (item.IsParentDirectory) return false;

        try
        {
            if (item.IsDirectory)
            {
                await Task.Run(() => Directory.Delete(item.FullPath, recursive), ct);
            }
            else
            {
                await Task.Run(() => File.Delete(item.FullPath), ct);
            }

            _logger.LogInformation("Deleted local item: {Path}", item.FullPath);
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

    private void LoadDrives()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new DriveInfoViewModel
                {
                    Name = d.Name,
                    Label = string.IsNullOrEmpty(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel,
                    RootPath = d.RootDirectory.FullName,
                    TotalSize = d.TotalSize,
                    FreeSpace = d.AvailableFreeSpace,
                    DriveType = d.DriveType
                })
                .ToList();

            Drives = new ObservableCollection<DriveInfoViewModel>(drives);
            _logger.LogDebug("Loaded {Count} drives", drives.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load drives");
        }
    }

    private void UpdateBreadcrumbs()
    {
        var segments = new List<BreadcrumbSegment>();

        if (string.IsNullOrEmpty(CurrentPath)) return;

        var parts = CurrentPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var currentFullPath = "";

        foreach (var part in parts)
        {
            currentFullPath = string.IsNullOrEmpty(currentFullPath)
                ? part + Path.DirectorySeparatorChar
                : Path.Combine(currentFullPath, part);

            segments.Add(new BreadcrumbSegment
            {
                Name = part,
                FullPath = currentFullPath
            });
        }

        Breadcrumbs = new ObservableCollection<BreadcrumbSegment>(segments);
    }
}

/// <summary>
/// ViewModel for a drive in the quick access list.
/// </summary>
public partial class DriveInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _rootPath = "";

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private long _freeSpace;

    [ObservableProperty]
    private DriveType _driveType;

    public string DisplayName => string.IsNullOrEmpty(Label) ? Name : $"{Label} ({Name})";

    public double UsedPercentage => TotalSize > 0 ? ((double)(TotalSize - FreeSpace) / TotalSize) * 100 : 0;
}

/// <summary>
/// Represents a segment in the path breadcrumb.
/// </summary>
public class BreadcrumbSegment
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
}
