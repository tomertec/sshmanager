using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for browsing the local filesystem.
/// Provides navigation, file listing, and drive/folder quick access.
/// </summary>
public partial class LocalFileBrowserViewModel : FileBrowserViewModelBase<DriveInfoViewModel>
{
    /// <inheritdoc />
    public override bool CanGoBack => _navigationHistory.Count > 0;

    /// <inheritdoc />
    public override bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) != null;

    /// <inheritdoc />
    protected override string BrowserTypeName => "local";

    /// <summary>
    /// Available drives for quick access (alias for QuickAccess).
    /// </summary>
    public ObservableCollection<DriveInfoViewModel> Drives => QuickAccess;

    public LocalFileBrowserViewModel(ILogger<LocalFileBrowserViewModel>? logger = null)
        : base(logger ?? NullLogger<LocalFileBrowserViewModel>.Instance)
    {
        LoadDrives();
    }

    /// <inheritdoc />
    public override async Task InitializeAsync()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await NavigateToAsync(userProfile);
    }

    /// <inheritdoc />
    protected override string NormalizePath(string path) => Path.GetFullPath(path);

    /// <inheritdoc />
    protected override string GetParentPath(string path)
    {
        var parent = Directory.GetParent(path);
        return parent?.FullName ?? path;
    }

    /// <inheritdoc />
    protected override string CombinePaths(string basePath, string relativePath)
        => Path.Combine(basePath, relativePath);

    /// <inheritdoc />
    protected override Task<bool> DirectoryExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(Directory.Exists(path));

    /// <inheritdoc />
    protected override bool IsRootPath(string path)
        => Directory.GetParent(path) == null;

    /// <inheritdoc />
    protected override async Task<List<FileItemViewModel>> LoadDirectoryItemsAsync(string path, CancellationToken ct = default)
    {
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
        }, ct);

        return items;
    }

    /// <inheritdoc />
    protected override void UpdateBreadcrumbs()
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

    /// <inheritdoc />
    public override async Task<bool> RenameAsync(FileItemViewModel item, string newName, CancellationToken ct = default)
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

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(FileItemViewModel item, bool recursive = false, CancellationToken ct = default)
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

            QuickAccess = new ObservableCollection<DriveInfoViewModel>(drives);
            _logger.LogDebug("Loaded {Count} drives", drives.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load drives");
        }
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
