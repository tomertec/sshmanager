using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.Services;
using SshManager.App.Views.Windows;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// Manages file operations like delete, edit, upload, and download.
/// </summary>
public partial class SftpFileOperationsViewModel : ObservableObject
{
    private readonly ILogger<SftpFileOperationsViewModel> _logger;
    private readonly ISftpSession _session;
    private readonly string _hostname;
    private readonly IEditorThemeService _editorThemeService;

    /// <summary>
    /// Callback to get the selected local item.
    /// </summary>
    public Func<FileItemViewModel?>? GetSelectedLocalItemCallback { get; set; }

    /// <summary>
    /// Callback to get the selected remote item.
    /// </summary>
    public Func<FileItemViewModel?>? GetSelectedRemoteItemCallback { get; set; }

    /// <summary>
    /// Callback to get selected local items.
    /// </summary>
    public Func<IReadOnlyList<FileItemViewModel>>? GetSelectedLocalItemsCallback { get; set; }

    /// <summary>
    /// Callback to get selected remote items.
    /// </summary>
    public Func<IReadOnlyList<FileItemViewModel>>? GetSelectedRemoteItemsCallback { get; set; }

    /// <summary>
    /// Callback to refresh the local browser.
    /// </summary>
    public Func<Task>? RefreshLocalBrowserCallback { get; set; }

    /// <summary>
    /// Callback to refresh the remote browser.
    /// </summary>
    public Func<Task>? RefreshRemoteBrowserCallback { get; set; }

    /// <summary>
    /// Callback to delete a remote item.
    /// </summary>
    public Func<FileItemViewModel, bool, Task<bool>>? DeleteRemoteCallback { get; set; }

    /// <summary>
    /// Callback to get the remote error message.
    /// </summary>
    public Func<string?>? GetRemoteErrorMessageCallback { get; set; }

    /// <summary>
    /// Callback to upload files.
    /// </summary>
    public Action<IReadOnlyList<string>>? UploadFilesCallback { get; set; }

    /// <summary>
    /// Callback to download files.
    /// </summary>
    public Action<IReadOnlyList<string>>? DownloadFilesCallback { get; set; }

    /// <summary>
    /// Callback to get the current local path.
    /// </summary>
    public Func<string>? GetCurrentLocalPathCallback { get; set; }

    /// <summary>
    /// Callback to get the remote browser session.
    /// </summary>
    public Func<ISftpSession?>? GetRemoteBrowserSessionCallback { get; set; }

    /// <summary>
    /// Action to set the main error message.
    /// </summary>
    public Action<string?>? SetErrorMessageAction { get; set; }

    /// <summary>
    /// Callback to show the delete confirmation dialog.
    /// </summary>
    public Action<string, bool, int, bool, Func<Task>>? ShowDeleteDialogCallback { get; set; }

    public SftpFileOperationsViewModel(
        ISftpSession session,
        string hostname,
        IEditorThemeService editorThemeService,
        ILogger<SftpFileOperationsViewModel>? logger = null)
    {
        _session = session;
        _hostname = hostname;
        _editorThemeService = editorThemeService;
        _logger = logger ?? NullLogger<SftpFileOperationsViewModel>.Instance;
    }

    /// <summary>
    /// Deletes the selected local item after confirmation.
    /// </summary>
    [RelayCommand]
    public Task DeleteLocalAsync()
    {
        var item = GetSelectedLocalItemCallback?.Invoke();
        if (item == null || item.IsParentDirectory) return Task.CompletedTask;

        if (ShowDeleteDialogCallback != null)
        {
            ShowDeleteDialogCallback(
                item.Name,
                false, // isRemote
                1, // itemCount
                item.IsDirectory,
                () => PerformDeleteLocalAsync(item));
        }

        return Task.CompletedTask;
    }

    private async Task PerformDeleteLocalAsync(FileItemViewModel item)
    {
        try
        {
            if (item.IsDirectory)
            {
                Directory.Delete(item.FullPath, recursive: true);
            }
            else
            {
                File.Delete(item.FullPath);
            }

            _logger.LogInformation("Deleted local item: {Path}", item.FullPath);

            if (RefreshLocalBrowserCallback != null)
            {
                await RefreshLocalBrowserCallback();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete local item: {Path}", item.FullPath);
            SetErrorMessageAction?.Invoke($"Failed to delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes the selected remote item after confirmation.
    /// </summary>
    [RelayCommand]
    public Task DeleteRemoteAsync()
    {
        var item = GetSelectedRemoteItemCallback?.Invoke();
        if (item == null || item.IsParentDirectory) return Task.CompletedTask;

        if (ShowDeleteDialogCallback != null)
        {
            ShowDeleteDialogCallback(
                item.Name,
                true, // isRemote
                1, // itemCount
                item.IsDirectory,
                () => PerformDeleteRemoteAsync(item));
        }

        return Task.CompletedTask;
    }

    private async Task PerformDeleteRemoteAsync(FileItemViewModel item)
    {
        if (DeleteRemoteCallback != null)
        {
            var success = await DeleteRemoteCallback(item, item.IsDirectory);
            if (!success && GetRemoteErrorMessageCallback != null)
            {
                SetErrorMessageAction?.Invoke(GetRemoteErrorMessageCallback());
            }
        }
    }

    /// <summary>
    /// Uploads the selected local files to the current remote directory.
    /// </summary>
    [RelayCommand]
    public void UploadSelected()
    {
        var allLocalItems = GetSelectedLocalItemsCallback?.Invoke() ?? [];
        var items = allLocalItems
            .Where(i => !i.IsParentDirectory && !i.IsDirectory)
            .ToList();

        if (items.Count == 0)
        {
            // Try single selection
            var item = GetSelectedLocalItemCallback?.Invoke();
            if (item != null && !item.IsParentDirectory && !item.IsDirectory)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            var hasDirectories = allLocalItems.Any(i => i.IsDirectory && !i.IsParentDirectory);
            var message = hasDirectories
                ? "Directory upload is not yet supported. Please select individual files."
                : "No files selected for upload";
            SetErrorMessageAction?.Invoke(message);
            return;
        }

        UploadFilesCallback?.Invoke(items.Select(i => i.FullPath).ToList());
    }

    /// <summary>
    /// Downloads the selected remote files to the current local directory.
    /// </summary>
    [RelayCommand]
    public void DownloadSelected()
    {
        var allRemoteItems = GetSelectedRemoteItemsCallback?.Invoke() ?? [];
        var items = allRemoteItems
            .Where(i => !i.IsParentDirectory && !i.IsDirectory)
            .ToList();

        if (items.Count == 0)
        {
            // Try single selection
            var item = GetSelectedRemoteItemCallback?.Invoke();
            if (item != null && !item.IsParentDirectory && !item.IsDirectory)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            var hasDirectories = allRemoteItems.Any(i => i.IsDirectory && !i.IsParentDirectory);
            var message = hasDirectories
                ? "Directory download is not yet supported. Please select individual files."
                : "No files selected for download";
            SetErrorMessageAction?.Invoke(message);
            return;
        }

        DownloadFilesCallback?.Invoke(items.Select(i => i.FullPath).ToList());
    }

    /// <summary>
    /// Opens a remote file in the text editor.
    /// </summary>
    /// <param name="item">The file item to edit.</param>
    /// <param name="ownerWindow">The owner window for the editor dialog.</param>
    public async Task EditRemoteFileAsync(FileItemViewModel item, System.Windows.Window ownerWindow)
    {
        if (item == null || item.IsDirectory || item.IsParentDirectory || !item.IsEditable)
        {
            _logger.LogWarning("Cannot edit item: {Name} (Directory: {IsDir}, Parent: {IsParent}, Editable: {IsEdit})",
                item?.Name, item?.IsDirectory, item?.IsParentDirectory, item?.IsEditable);
            return;
        }

        try
        {
            _logger.LogInformation("Opening remote file for editing: {Path}", item.FullPath);

            var viewModel = new TextEditorViewModel(_editorThemeService);

            // Get the SFTP session from the remote browser
            var session = GetRemoteBrowserSessionCallback?.Invoke();
            if (session == null || !session.IsConnected)
            {
                SetErrorMessageAction?.Invoke("SFTP session is not connected");
                return;
            }

            // Load the remote file
            await viewModel.LoadRemoteFileAsync(session, item.FullPath, _hostname);

            // Show the editor window
            var editorWindow = new TextEditorWindow(viewModel, _editorThemeService)
            {
                Owner = ownerWindow
            };

            editorWindow.ShowDialog();

            // Refresh the remote browser in case the file was modified
            if (RefreshRemoteBrowserCallback != null)
            {
                await RefreshRemoteBrowserCallback();
            }

            _logger.LogInformation("Closed editor for remote file: {Path}", item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open remote file for editing: {Path}", item.FullPath);
            SetErrorMessageAction?.Invoke($"Failed to open file: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a local file in the text editor.
    /// </summary>
    /// <param name="item">The file item to edit.</param>
    /// <param name="ownerWindow">The owner window for the editor dialog.</param>
    public async Task EditLocalFileAsync(FileItemViewModel item, System.Windows.Window ownerWindow)
    {
        if (item == null || item.IsDirectory || item.IsParentDirectory || !item.IsEditable)
        {
            _logger.LogWarning("Cannot edit item: {Name} (Directory: {IsDir}, Parent: {IsParent}, Editable: {IsEdit})",
                item?.Name, item?.IsDirectory, item?.IsParentDirectory, item?.IsEditable);
            return;
        }

        try
        {
            _logger.LogInformation("Opening local file for editing: {Path}", item.FullPath);

            var viewModel = new TextEditorViewModel(_editorThemeService);

            // Load the local file
            await viewModel.LoadLocalFileAsync(item.FullPath);

            // Show the editor window
            var editorWindow = new TextEditorWindow(viewModel, _editorThemeService)
            {
                Owner = ownerWindow
            };

            editorWindow.ShowDialog();

            // Refresh the local browser in case the file was modified
            if (RefreshLocalBrowserCallback != null)
            {
                await RefreshLocalBrowserCallback();
            }

            _logger.LogInformation("Closed editor for local file: {Path}", item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open local file for editing: {Path}", item.FullPath);
            SetErrorMessageAction?.Invoke($"Failed to open file: {ex.Message}");
        }
    }
}
