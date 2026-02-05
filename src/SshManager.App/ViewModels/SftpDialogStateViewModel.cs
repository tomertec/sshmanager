using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Core.Formatting;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// Manages dialog state and interactions for SFTP operations.
/// </summary>
public partial class SftpDialogStateViewModel : ObservableObject
{
    private readonly ILogger<SftpDialogStateViewModel> _logger;
    private readonly ISftpSession _session;
    private readonly List<FileItemViewModel> _permissionTargets = [];

    /// <summary>
    /// Whether the new folder dialog is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isNewFolderDialogVisible;

    /// <summary>
    /// The name for the new folder being created.
    /// </summary>
    [ObservableProperty]
    private string _newFolderName = "";

    /// <summary>
    /// Whether the new folder is being created on the remote side.
    /// </summary>
    [ObservableProperty]
    private bool _isNewFolderRemote;

    /// <summary>
    /// Whether the overwrite confirmation dialog is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isOverwriteDialogVisible;

    /// <summary>
    /// The name of the file being confirmed for overwrite.
    /// </summary>
    [ObservableProperty]
    private string _overwriteFileName = "";

    /// <summary>
    /// Whether the overwrite is for an upload (true) or download (false).
    /// </summary>
    [ObservableProperty]
    private bool _isOverwriteUpload;

    /// <summary>
    /// Whether to apply the overwrite decision to all remaining files.
    /// </summary>
    [ObservableProperty]
    private bool _overwriteApplyToAll;

    /// <summary>
    /// Existing file size when a conflict is detected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverwriteSizeDisplay))]
    private long _overwriteExistingSize;

    /// <summary>
    /// Total file size for the conflicting file.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverwriteSizeDisplay))]
    private long _overwriteTotalSize;

    /// <summary>
    /// Whether resume is available for the current conflict.
    /// </summary>
    [ObservableProperty]
    private bool _overwriteCanResume;

    /// <summary>
    /// Whether the permissions dialog is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isPermissionsDialogVisible;

    /// <summary>
    /// Current permissions input (octal).
    /// </summary>
    [ObservableProperty]
    private string _permissionsInput = "";

    /// <summary>
    /// Target name displayed in the permissions dialog.
    /// </summary>
    [ObservableProperty]
    private string _permissionsTargetName = "";

    /// <summary>
    /// Current permissions display in the dialog.
    /// </summary>
    [ObservableProperty]
    private string _permissionsCurrentDisplay = "";

    /// <summary>
    /// Error message for permissions changes.
    /// </summary>
    [ObservableProperty]
    private string? _permissionsErrorMessage;

    /// <summary>
    /// Whether the delete confirmation dialog is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isDeleteDialogVisible;

    /// <summary>
    /// The name of the item(s) being deleted.
    /// </summary>
    [ObservableProperty]
    private string _deleteTargetName = "";

    /// <summary>
    /// Whether the delete is for remote files (true) or local files (false).
    /// </summary>
    [ObservableProperty]
    private bool _isDeleteRemote;

    /// <summary>
    /// Number of items being deleted.
    /// </summary>
    [ObservableProperty]
    private int _deleteItemCount;

    /// <summary>
    /// Whether deleting a directory (recursive delete warning).
    /// </summary>
    [ObservableProperty]
    private bool _isDeleteDirectory;

    /// <summary>
    /// Callback to perform the actual delete operation after confirmation.
    /// </summary>
    private Func<Task>? _pendingDeleteAction;

    public string OverwriteSizeDisplay => OverwriteTotalSize > 0
        ? $"Existing: {FormatFileSize(OverwriteExistingSize)} of {FormatFileSize(OverwriteTotalSize)}"
        : $"Existing: {FormatFileSize(OverwriteExistingSize)}";

    /// <summary>
    /// Callback to refresh the local browser.
    /// </summary>
    public Func<Task>? RefreshLocalBrowserCallback { get; set; }

    /// <summary>
    /// Callback to create a remote directory.
    /// </summary>
    public Func<string, Task<bool>>? CreateRemoteDirectoryCallback { get; set; }

    /// <summary>
    /// Callback to get the current local path.
    /// </summary>
    public Func<string>? GetCurrentLocalPathCallback { get; set; }

    /// <summary>
    /// Callback to get the remote error message.
    /// </summary>
    public Func<string?>? GetRemoteErrorMessageCallback { get; set; }

    /// <summary>
    /// Callback to refresh the remote browser.
    /// </summary>
    public Func<Task>? RefreshRemoteBrowserCallback { get; set; }

    /// <summary>
    /// Action to set the main error message.
    /// </summary>
    public Action<string?>? SetErrorMessageAction { get; set; }

    public SftpDialogStateViewModel(
        ISftpSession session,
        ILogger<SftpDialogStateViewModel>? logger = null)
    {
        _session = session;
        _logger = logger ?? NullLogger<SftpDialogStateViewModel>.Instance;
    }

    /// <summary>
    /// Shows the new folder dialog for the local browser.
    /// </summary>
    [RelayCommand]
    public void ShowNewLocalFolder()
    {
        NewFolderName = "";
        IsNewFolderRemote = false;
        IsNewFolderDialogVisible = true;
    }

    /// <summary>
    /// Shows the new folder dialog for the remote browser.
    /// </summary>
    [RelayCommand]
    public void ShowNewRemoteFolder()
    {
        NewFolderName = "";
        IsNewFolderRemote = true;
        IsNewFolderDialogVisible = true;
    }

    /// <summary>
    /// Creates a new folder with the specified name.
    /// </summary>
    [RelayCommand]
    public async Task CreateNewFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
        {
            return;
        }

        // Sanitize folder name - reject path separators and dangerous characters
        var folderName = NewFolderName.Trim();
        if (folderName.Contains('/')
            || folderName.Contains('\\')
            || folderName.Contains('\0')
            || folderName.Contains("..")
            || folderName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            SetErrorMessageAction?.Invoke("Folder name contains invalid characters.");
            return;
        }

        SetErrorMessageAction?.Invoke(null);

        try
        {
            if (IsNewFolderRemote)
            {
                if (CreateRemoteDirectoryCallback != null)
                {
                    var success = await CreateRemoteDirectoryCallback(folderName);
                    if (!success && GetRemoteErrorMessageCallback != null)
                    {
                        SetErrorMessageAction?.Invoke(GetRemoteErrorMessageCallback());
                    }
                }
            }
            else
            {
                // Create local directory
                if (GetCurrentLocalPathCallback != null)
                {
                    var currentPath = GetCurrentLocalPathCallback();
                    var newPath = Path.Combine(currentPath, folderName);
                    Directory.CreateDirectory(newPath);
                    _logger.LogInformation("Created local directory: {Path}", newPath);

                    if (RefreshLocalBrowserCallback != null)
                    {
                        await RefreshLocalBrowserCallback();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create folder: {Name}", folderName);
            SetErrorMessageAction?.Invoke($"Failed to create folder: {ex.Message}");
        }
        finally
        {
            IsNewFolderDialogVisible = false;
            NewFolderName = "";
        }
    }

    /// <summary>
    /// Cancels the new folder dialog.
    /// </summary>
    [RelayCommand]
    public void CancelNewFolder()
    {
        IsNewFolderDialogVisible = false;
        NewFolderName = "";
    }

    /// <summary>
    /// Shows the overwrite confirmation dialog.
    /// </summary>
    public void ShowOverwriteDialog(
        string fileName,
        bool isUpload,
        long existingSize,
        long totalSize,
        bool canResume)
    {
        OverwriteFileName = fileName;
        IsOverwriteUpload = isUpload;
        OverwriteExistingSize = existingSize;
        OverwriteTotalSize = totalSize;
        OverwriteCanResume = canResume;
        IsOverwriteDialogVisible = true;
    }

    /// <summary>
    /// Hides the overwrite confirmation dialog.
    /// </summary>
    public void HideOverwriteDialog()
    {
        IsOverwriteDialogVisible = false;
        OverwriteApplyToAll = false;
    }

    /// <summary>
    /// Opens the permissions dialog for selected remote items.
    /// </summary>
    public void ShowPermissionsDialog(IReadOnlyList<FileItemViewModel> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        _permissionTargets.Clear();
        _permissionTargets.AddRange(targets);

        PermissionsErrorMessage = null;
        PermissionsTargetName = targets.Count == 1 ? targets[0].Name : $"{targets.Count} items";

        var distinct = targets
            .Select(t => t.PermissionsOctal)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinct.Count == 1)
        {
            var normalized = distinct[0];
            if (normalized.StartsWith("0", StringComparison.Ordinal) && normalized.Length == 4)
            {
                normalized = normalized[1..];
            }
            PermissionsInput = normalized;
            PermissionsCurrentDisplay = $"{targets[0].PermissionsDisplay} ({distinct[0]})";
        }
        else
        {
            PermissionsInput = "";
            PermissionsCurrentDisplay = targets.Count > 1 ? "Mixed" : "Unknown";
        }

        IsPermissionsDialogVisible = true;
    }

    /// <summary>
    /// Applies permissions to selected remote items.
    /// </summary>
    [RelayCommand]
    public async Task ApplyPermissionsAsync()
    {
        PermissionsErrorMessage = null;

        if (_permissionTargets.Count == 0)
        {
            PermissionsErrorMessage = "No items selected.";
            return;
        }

        if (!TryParsePermissions(PermissionsInput, out var permissions))
        {
            PermissionsErrorMessage = "Enter permissions in octal format (e.g. 755).";
            return;
        }

        try
        {
            foreach (var item in _permissionTargets)
            {
                await _session.ChangePermissionsAsync(item.FullPath, permissions);
            }

            IsPermissionsDialogVisible = false;

            if (RefreshRemoteBrowserCallback != null)
            {
                await RefreshRemoteBrowserCallback();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change permissions");
            PermissionsErrorMessage = $"Failed to change permissions: {ex.Message}";
        }
    }

    /// <summary>
    /// Cancels permissions changes.
    /// </summary>
    [RelayCommand]
    public void CancelPermissions()
    {
        IsPermissionsDialogVisible = false;
        PermissionsErrorMessage = null;
    }

    /// <summary>
    /// Shows the delete confirmation dialog.
    /// </summary>
    /// <param name="targetName">Display name for the target item(s).</param>
    /// <param name="isRemote">Whether deleting remote files.</param>
    /// <param name="itemCount">Number of items being deleted.</param>
    /// <param name="isDirectory">Whether any item is a directory.</param>
    /// <param name="deleteAction">Action to perform if confirmed.</param>
    public void ShowDeleteDialog(
        string targetName,
        bool isRemote,
        int itemCount,
        bool isDirectory,
        Func<Task> deleteAction)
    {
        DeleteTargetName = targetName;
        IsDeleteRemote = isRemote;
        DeleteItemCount = itemCount;
        IsDeleteDirectory = isDirectory;
        _pendingDeleteAction = deleteAction;
        IsDeleteDialogVisible = true;
    }

    /// <summary>
    /// Confirms the delete operation.
    /// </summary>
    [RelayCommand]
    public async Task ConfirmDeleteAsync()
    {
        IsDeleteDialogVisible = false;

        if (_pendingDeleteAction != null)
        {
            try
            {
                await _pendingDeleteAction();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete item(s)");
                SetErrorMessageAction?.Invoke($"Failed to delete: {ex.Message}");
            }
        }

        _pendingDeleteAction = null;
    }

    /// <summary>
    /// Cancels the delete operation.
    /// </summary>
    [RelayCommand]
    public void CancelDelete()
    {
        IsDeleteDialogVisible = false;
        _pendingDeleteAction = null;
    }

    private static bool TryParsePermissions(string? input, out int permissions)
    {
        permissions = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Length == 4 && trimmed.StartsWith("0", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length < 3 || trimmed.Length > 4)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            if (ch < '0' || ch > '7')
            {
                return false;
            }
        }

        permissions = Convert.ToInt32(trimmed, 8);
        return true;
    }

    private static string FormatFileSize(long bytes) => FileSizeFormatter.FormatSize(bytes);
}
