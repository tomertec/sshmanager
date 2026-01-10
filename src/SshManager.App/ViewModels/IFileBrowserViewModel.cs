using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SshManager.App.ViewModels;

/// <summary>
/// Interface for file browser view models, providing common navigation and file operations.
/// Implemented by both LocalFileBrowserViewModel and RemoteFileBrowserViewModel.
/// </summary>
public interface IFileBrowserViewModel
{
    /// <summary>
    /// Current directory path.
    /// </summary>
    string CurrentPath { get; }

    /// <summary>
    /// Items in the current directory.
    /// </summary>
    ObservableCollection<FileItemViewModel> Items { get; }

    /// <summary>
    /// Currently selected item.
    /// </summary>
    FileItemViewModel? SelectedItem { get; set; }

    /// <summary>
    /// Currently selected items (for multi-select).
    /// </summary>
    ObservableCollection<FileItemViewModel> SelectedItems { get; }

    /// <summary>
    /// Whether the browser is currently loading.
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// Error message if loading failed.
    /// </summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// Whether navigation back is available.
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Whether navigation up is available.
    /// </summary>
    bool CanGoUp { get; }

    /// <summary>
    /// Breadcrumb segments for path navigation.
    /// </summary>
    ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; }

    /// <summary>
    /// Command to navigate to a path.
    /// </summary>
    IAsyncRelayCommand<string> NavigateToCommand { get; }

    /// <summary>
    /// Command to open an item (navigate to directory or open file).
    /// </summary>
    IAsyncRelayCommand<FileItemViewModel> OpenItemCommand { get; }

    /// <summary>
    /// Command to navigate back in history.
    /// </summary>
    IAsyncRelayCommand GoBackCommand { get; }

    /// <summary>
    /// Command to navigate up to parent directory.
    /// </summary>
    IAsyncRelayCommand GoUpCommand { get; }

    /// <summary>
    /// Command to refresh the current directory.
    /// </summary>
    IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Command to navigate to a breadcrumb segment.
    /// </summary>
    IAsyncRelayCommand<BreadcrumbSegment> NavigateToBreadcrumbCommand { get; }

    /// <summary>
    /// Command to copy an item's path to clipboard.
    /// </summary>
    IRelayCommand<FileItemViewModel> CopyPathCommand { get; }

    /// <summary>
    /// Command to sort items by a column.
    /// </summary>
    IRelayCommand<FileSortColumn> SortByCommand { get; }

    /// <summary>
    /// Current sort column.
    /// </summary>
    FileSortColumn SortColumn { get; }

    /// <summary>
    /// Current sort direction.
    /// </summary>
    ListSortDirection SortDirection { get; }

    /// <summary>
    /// Filter text for searching files by name.
    /// </summary>
    string FilterText { get; set; }

    /// <summary>
    /// Command to clear the filter.
    /// </summary>
    IRelayCommand ClearFilterCommand { get; }

    /// <summary>
    /// Initializes the browser (navigates to initial directory).
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Renames the specified item.
    /// </summary>
    Task<bool> RenameAsync(FileItemViewModel item, string newName, CancellationToken ct = default);

    /// <summary>
    /// Deletes the specified item.
    /// </summary>
    Task<bool> DeleteAsync(FileItemViewModel item, bool recursive = false, CancellationToken ct = default);
}
