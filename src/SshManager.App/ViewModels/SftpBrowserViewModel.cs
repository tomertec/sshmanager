using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Terminal.Services;
using SshManager.App.Services;
using SshManager.App.Views.Windows;

namespace SshManager.App.ViewModels;

/// <summary>
/// Main ViewModel for the SFTP file browser panel.
/// Composes local and remote file browsers and manages file transfers.
/// </summary>
public partial class SftpBrowserViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ILogger<SftpBrowserViewModel> _logger;
    private readonly ISftpSession _session;
    private bool _isProcessingQueue;
    private ConflictResolution? _applyConflictResolution;
    private readonly List<FileItemViewModel> _permissionTargets = [];

    /// <summary>
    /// Delay in milliseconds before auto-removing completed transfers.
    /// </summary>
    private const int AutoRemoveDelayMs = 5000;

    private enum ConflictResolution
    {
        Overwrite,
        Skip,
        Resume,
        KeepBoth
    }

    /// <summary>
    /// The local file browser view model.
    /// </summary>
    [ObservableProperty]
    private LocalFileBrowserViewModel _localBrowser;

    /// <summary>
    /// The remote file browser view model.
    /// </summary>
    [ObservableProperty]
    private RemoteFileBrowserViewModel _remoteBrowser;

    /// <summary>
    /// Active and recent transfers.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _transfers = [];

    /// <summary>
    /// Whether the SFTP session is connected.
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// The hostname of the connected server.
    /// </summary>
    [ObservableProperty]
    private string _hostname = "";

    /// <summary>
    /// Error message if an operation failed.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

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
    /// Pending files to transfer after overwrite confirmation.
    /// </summary>
    private List<(string LocalPath, string RemotePath)> _pendingTransfers = [];

    /// <summary>
    /// Current index in the pending transfers being confirmed.
    /// </summary>
    private int _currentOverwriteIndex;

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
    /// Whether a transfer is currently in progress.
    /// </summary>
    public bool HasActiveTransfer => Transfers.Any(t =>
        t.Status == TransferStatus.InProgress || t.Status == TransferStatus.Pending);

    public string OverwriteSizeDisplay => OverwriteTotalSize > 0
        ? $"Existing: {FormatFileSize(OverwriteExistingSize)} of {FormatFileSize(OverwriteTotalSize)}"
        : $"Existing: {FormatFileSize(OverwriteExistingSize)}";

    /// <summary>
    /// Event raised when the session is disconnected.
    /// </summary>
    public event EventHandler? Disconnected;

    public SftpBrowserViewModel(
        ISftpSession session,
        string hostname,
        ILogger<SftpBrowserViewModel>? logger = null,
        ILogger<LocalFileBrowserViewModel>? localLogger = null,
        ILogger<RemoteFileBrowserViewModel>? remoteLogger = null)
    {
        _session = session;
        _logger = logger ?? NullLogger<SftpBrowserViewModel>.Instance;

        Hostname = hostname;
        IsConnected = session.IsConnected;

        // Initialize child view models
        _localBrowser = new LocalFileBrowserViewModel(localLogger);
        _remoteBrowser = new RemoteFileBrowserViewModel(remoteLogger);

        // Set up the remote browser with the session
        _remoteBrowser.SetSession(session);
        _remoteBrowser.PropertyChanged += OnRemoteBrowserPropertyChanged;

        // Subscribe to session disconnect
        _session.Disconnected += OnSessionDisconnected;

        _logger.LogDebug("SftpBrowserViewModel created for {Hostname}", hostname);
    }

    /// <summary>
    /// Initializes both local and remote browsers.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogDebug("Initializing SFTP browsers");

        try
        {
            // Initialize both browsers in parallel
            await Task.WhenAll(
                LocalBrowser.InitializeAsync(),
                RemoteBrowser.InitializeAsync()
            );

            _logger.LogInformation("SFTP browsers initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SFTP browsers");
            ErrorMessage = $"Failed to initialize: {ex.Message}";
        }
    }

    /// <summary>
    /// Refreshes both local and remote directory listings.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        _logger.LogDebug("Refreshing all browsers");
        ErrorMessage = null;

        try
        {
            await Task.WhenAll(
                LocalBrowser.RefreshAsync(),
                RemoteBrowser.RefreshAsync()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh browsers");
            ErrorMessage = $"Refresh failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Refreshes the local browser.
    /// </summary>
    [RelayCommand]
    private async Task RefreshLocalAsync()
    {
        await LocalBrowser.RefreshAsync();
    }

    /// <summary>
    /// Refreshes the remote browser.
    /// </summary>
    [RelayCommand]
    private async Task RefreshRemoteAsync()
    {
        await RemoteBrowser.RefreshAsync();
    }

    /// <summary>
    /// Shows the new folder dialog for the local browser.
    /// </summary>
    [RelayCommand]
    private void ShowNewLocalFolder()
    {
        NewFolderName = "";
        IsNewFolderRemote = false;
        IsNewFolderDialogVisible = true;
    }

    /// <summary>
    /// Shows the new folder dialog for the remote browser.
    /// </summary>
    [RelayCommand]
    private void ShowNewRemoteFolder()
    {
        NewFolderName = "";
        IsNewFolderRemote = true;
        IsNewFolderDialogVisible = true;
    }

    /// <summary>
    /// Creates a new folder with the specified name.
    /// </summary>
    [RelayCommand]
    private async Task CreateNewFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            if (IsNewFolderRemote)
            {
                var success = await RemoteBrowser.CreateDirectoryAsync(NewFolderName);
                if (!success)
                {
                    ErrorMessage = RemoteBrowser.ErrorMessage;
                }
            }
            else
            {
                // Create local directory
                var newPath = Path.Combine(LocalBrowser.CurrentPath, NewFolderName);
                Directory.CreateDirectory(newPath);
                _logger.LogInformation("Created local directory: {Path}", newPath);
                await LocalBrowser.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create folder: {Name}", NewFolderName);
            ErrorMessage = $"Failed to create folder: {ex.Message}";
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
    private void CancelNewFolder()
    {
        IsNewFolderDialogVisible = false;
        NewFolderName = "";
    }

    /// <summary>
    /// Confirms overwriting the current file and continues with transfer.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmOverwriteAsync()
    {
        await ApplyCurrentConflictResolutionAsync(ConflictResolution.Overwrite);
    }

    /// <summary>
    /// Skips the current file and continues with the next.
    /// </summary>
    [RelayCommand]
    private async Task SkipOverwriteAsync()
    {
        await ApplyCurrentConflictResolutionAsync(ConflictResolution.Skip);
    }

    /// <summary>
    /// Resumes the current transfer if possible.
    /// </summary>
    [RelayCommand]
    private async Task ResumeOverwriteAsync()
    {
        if (!OverwriteCanResume) return;
        await ApplyCurrentConflictResolutionAsync(ConflictResolution.Resume);
    }

    /// <summary>
    /// Keeps both files by renaming the incoming transfer.
    /// </summary>
    [RelayCommand]
    private async Task KeepBothOverwriteAsync()
    {
        await ApplyCurrentConflictResolutionAsync(ConflictResolution.KeepBoth);
    }

    /// <summary>
    /// Cancels all pending transfers.
    /// </summary>
    [RelayCommand]
    private void CancelOverwrite()
    {
        IsOverwriteDialogVisible = false;
        _pendingTransfers.Clear();
        _currentOverwriteIndex = 0;
        OverwriteApplyToAll = false;
        _applyConflictResolution = null;
    }

    private async Task ApplyCurrentConflictResolutionAsync(ConflictResolution resolution)
    {
        IsOverwriteDialogVisible = false;

        if (OverwriteApplyToAll)
        {
            _applyConflictResolution = resolution;
        }

        var (localPath, remotePath) = _pendingTransfers[_currentOverwriteIndex];
        await ApplyConflictResolutionAsync(
            resolution,
            localPath,
            remotePath,
            IsOverwriteUpload,
            OverwriteExistingSize,
            OverwriteTotalSize);

        _currentOverwriteIndex++;

        if (IsOverwriteUpload)
        {
            await ProcessPendingUploadsAsync();
        }
        else
        {
            await ProcessPendingDownloadsAsync();
        }
    }

    /// <summary>
    /// Deletes the selected local item.
    /// </summary>
    [RelayCommand]
    private async Task DeleteLocalAsync()
    {
        var item = LocalBrowser.SelectedItem;
        if (item == null || item.IsParentDirectory) return;

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
            await LocalBrowser.RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete local item: {Path}", item.FullPath);
            ErrorMessage = $"Failed to delete: {ex.Message}";
        }
    }

    /// <summary>
    /// Deletes the selected remote item.
    /// </summary>
    [RelayCommand]
    private async Task DeleteRemoteAsync()
    {
        var item = RemoteBrowser.SelectedItem;
        if (item == null || item.IsParentDirectory) return;

        var success = await RemoteBrowser.DeleteAsync(item, recursive: item.IsDirectory);
        if (!success)
        {
            ErrorMessage = RemoteBrowser.ErrorMessage;
        }
    }

    /// <summary>
    /// Opens the permissions dialog for selected remote items.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowPermissionsDialog))]
    private void ShowPermissionsDialog()
    {
        var targets = GetPermissionTargets();
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

    private bool CanShowPermissionsDialog()
    {
        var item = RemoteBrowser.SelectedItem;
        return IsConnected && item != null && !item.IsParentDirectory;
    }

    /// <summary>
    /// Applies permissions to selected remote items.
    /// </summary>
    [RelayCommand]
    private async Task ApplyPermissionsAsync()
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
            await RemoteBrowser.RefreshAsync();
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
    private void CancelPermissions()
    {
        IsPermissionsDialogVisible = false;
        PermissionsErrorMessage = null;
    }

    /// <summary>
    /// Uploads the selected local files to the current remote directory.
    /// </summary>
    [RelayCommand]
    private void UploadSelected()
    {
        var items = LocalBrowser.SelectedItems.Where(i => !i.IsParentDirectory && !i.IsDirectory).ToList();
        if (items.Count == 0)
        {
            // Try single selection
            var item = LocalBrowser.SelectedItem;
            if (item != null && !item.IsParentDirectory && !item.IsDirectory)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            ErrorMessage = "No files selected for upload";
            return;
        }

        UploadFiles(items.Select(i => i.FullPath).ToList());
    }

    /// <summary>
    /// Downloads the selected remote files to the current local directory.
    /// </summary>
    [RelayCommand]
    private void DownloadSelected()
    {
        var items = RemoteBrowser.SelectedItems.Where(i => !i.IsParentDirectory && !i.IsDirectory).ToList();
        if (items.Count == 0)
        {
            // Try single selection
            var item = RemoteBrowser.SelectedItem;
            if (item != null && !item.IsParentDirectory && !item.IsDirectory)
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            ErrorMessage = "No files selected for download";
            return;
        }

        DownloadFiles(items.Select(i => i.FullPath).ToList());
    }

    /// <summary>
    /// Uploads files from local paths to the current remote directory.
    /// Checks for existing files and prompts for overwrite confirmation.
    /// </summary>
    public void UploadFiles(IReadOnlyList<string> localPaths)
    {
        _ = UploadFilesWithConfirmationAsync(localPaths);
    }

    private async Task UploadFilesWithConfirmationAsync(IReadOnlyList<string> localPaths)
    {
        _pendingTransfers.Clear();
        _currentOverwriteIndex = 0;
        OverwriteApplyToAll = false;
        _applyConflictResolution = null;

        // Build list of transfers with remote paths
        foreach (var localPath in localPaths)
        {
            var fileName = Path.GetFileName(localPath);
            var remotePath = RemoteBrowser.CurrentPath == "/"
                ? "/" + fileName
                : RemoteBrowser.CurrentPath + "/" + fileName;

            _pendingTransfers.Add((localPath, remotePath));
        }

        // Check each file for existence and start transfers
        await ProcessPendingUploadsAsync();
    }

    private async Task ProcessPendingUploadsAsync()
    {
        while (_currentOverwriteIndex < _pendingTransfers.Count)
        {
            var (localPath, remotePath) = _pendingTransfers[_currentOverwriteIndex];

            // Check if remote file exists
            SftpFileItem? existingInfo = null;
            try
            {
                existingInfo = await _session.GetFileInfoAsync(remotePath);
            }
            catch
            {
                // Ignore errors checking existence, proceed with transfer
            }

            var exists = existingInfo != null;
            var totalBytes = new FileInfo(localPath).Length;
            var existingSize = existingInfo?.Size ?? 0;

            if (exists && _applyConflictResolution.HasValue)
            {
                await ApplyConflictResolutionAsync(
                    _applyConflictResolution.Value,
                    localPath,
                    remotePath,
                    isUpload: true,
                    existingSize,
                    totalBytes);
                _currentOverwriteIndex++;
                continue;
            }

            if (exists)
            {
                // Show confirmation dialog
                OverwriteFileName = Path.GetFileName(localPath);
                IsOverwriteUpload = true;
                OverwriteExistingSize = existingSize;
                OverwriteTotalSize = totalBytes;
                OverwriteCanResume = existingSize > 0 && totalBytes > 0 && existingSize < totalBytes;
                IsOverwriteDialogVisible = true;
                return; // Wait for user response
            }

            // Start the transfer
            StartUploadTransfer(localPath, remotePath);
            _currentOverwriteIndex++;
        }

        // All transfers started
        _pendingTransfers.Clear();
        OverwriteApplyToAll = false;
        _applyConflictResolution = null;
    }

    private void StartUploadTransfer(string localPath, string remotePath, long resumeOffset = 0)
    {
        var fileName = Path.GetFileName(localPath);
        var fileInfo = new FileInfo(localPath);
        var totalBytes = fileInfo.Length;
        var transferItem = new TransferItemViewModel
        {
            FileName = fileName,
            LocalPath = localPath,
            RemotePath = remotePath,
            Direction = TransferDirection.Upload,
            TotalBytes = totalBytes,
            Status = TransferStatus.Pending,
            ResumeOffset = Math.Clamp(resumeOffset, 0, totalBytes)
        };

        InitializeTransferProgress(transferItem);
        EnqueueTransfer(transferItem);
    }

    /// <summary>
    /// Downloads files from remote paths to the current local directory.
    /// Checks for existing files and prompts for overwrite confirmation.
    /// </summary>
    public void DownloadFiles(IReadOnlyList<string> remotePaths)
    {
        _ = DownloadFilesWithConfirmationAsync(remotePaths);
    }

    private async Task DownloadFilesWithConfirmationAsync(IReadOnlyList<string> remotePaths)
    {
        _pendingTransfers.Clear();
        _currentOverwriteIndex = 0;
        OverwriteApplyToAll = false;
        _applyConflictResolution = null;

        // Build list of transfers with local paths
        foreach (var remotePath in remotePaths)
        {
            var fileName = Path.GetFileName(remotePath);
            var localPath = Path.Combine(LocalBrowser.CurrentPath, fileName);

            _pendingTransfers.Add((localPath, remotePath));
        }

        // Check each file for existence and start transfers
        await ProcessPendingDownloadsAsync();
    }

    private async Task ProcessPendingDownloadsAsync()
    {
        while (_currentOverwriteIndex < _pendingTransfers.Count)
        {
            var (localPath, remotePath) = _pendingTransfers[_currentOverwriteIndex];

            // Check if local file exists
            bool exists = File.Exists(localPath);
            var existingSize = exists ? new FileInfo(localPath).Length : 0;
            var totalBytes = await GetRemoteFileSizeAsync(remotePath);

            if (exists && _applyConflictResolution.HasValue)
            {
                await ApplyConflictResolutionAsync(
                    _applyConflictResolution.Value,
                    localPath,
                    remotePath,
                    isUpload: false,
                    existingSize,
                    totalBytes);
                _currentOverwriteIndex++;
                continue;
            }

            if (exists)
            {
                // Show confirmation dialog
                OverwriteFileName = Path.GetFileName(localPath);
                IsOverwriteUpload = false;
                OverwriteExistingSize = existingSize;
                OverwriteTotalSize = totalBytes;
                OverwriteCanResume = existingSize > 0 && totalBytes > 0 && existingSize < totalBytes;
                IsOverwriteDialogVisible = true;
                return; // Wait for user response
            }

            // Start the transfer
            await StartDownloadTransferAsync(localPath, remotePath, resumeOffset: 0, totalBytesOverride: totalBytes);
            _currentOverwriteIndex++;
        }

        // All transfers started
        _pendingTransfers.Clear();
        OverwriteApplyToAll = false;
        _applyConflictResolution = null;
    }

    private async Task ApplyConflictResolutionAsync(
        ConflictResolution resolution,
        string localPath,
        string remotePath,
        bool isUpload,
        long existingSize,
        long totalBytes)
    {
        if (resolution == ConflictResolution.Resume)
        {
            if (totalBytes > 0 && existingSize >= totalBytes)
            {
                resolution = ConflictResolution.Skip;
            }
            else if (existingSize <= 0 || totalBytes <= 0)
            {
                resolution = ConflictResolution.Overwrite;
            }
        }

        switch (resolution)
        {
            case ConflictResolution.Skip:
                return;
            case ConflictResolution.Resume:
                if (existingSize <= 0 || totalBytes <= 0 || existingSize >= totalBytes)
                {
                    return;
                }

                if (isUpload)
                {
                    StartUploadTransfer(localPath, remotePath, existingSize);
                }
                else
                {
                    await StartDownloadTransferAsync(localPath, remotePath, existingSize, totalBytes);
                }
                return;
            case ConflictResolution.KeepBoth:
            {
                if (isUpload)
                {
                    var uniqueRemotePath = await GetUniqueRemotePathAsync(remotePath);
                    StartUploadTransfer(localPath, uniqueRemotePath);
                }
                else
                {
                    var uniqueLocalPath = GetUniqueLocalPath(localPath);
                    await StartDownloadTransferAsync(uniqueLocalPath, remotePath, 0, totalBytes);
                }
                return;
            }
            case ConflictResolution.Overwrite:
            default:
                if (isUpload)
                {
                    StartUploadTransfer(localPath, remotePath);
                }
                else
                {
                    await StartDownloadTransferAsync(localPath, remotePath, 0, totalBytes);
                }
                return;
        }
    }

    private async Task<long> GetRemoteFileSizeAsync(string remotePath)
    {
        var remoteItem = RemoteBrowser.Items.FirstOrDefault(i => i.FullPath == remotePath);
        if (remoteItem?.Size > 0)
        {
            return remoteItem.Size;
        }

        try
        {
            var info = await _session.GetFileInfoAsync(remotePath);
            return info?.Size ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetUniqueLocalPath(string localPath)
    {
        var directory = Path.GetDirectoryName(localPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(localPath);
        var extension = Path.GetExtension(localPath);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }

    private async Task<string> GetUniqueRemotePathAsync(string remotePath)
    {
        var directory = GetRemoteDirectory(remotePath);
        var name = Path.GetFileNameWithoutExtension(remotePath);
        var extension = Path.GetExtension(remotePath);

        for (var i = 1; i < 1000; i++)
        {
            var candidateName = $"{name} ({i}){extension}";
            var candidatePath = directory == "/" ? $"/{candidateName}" : $"{directory}/{candidateName}";
            if (!await _session.ExistsAsync(candidatePath))
            {
                return candidatePath;
            }
        }

        var fallbackName = $"{name} ({Guid.NewGuid():N}){extension}";
        return directory == "/" ? $"/{fallbackName}" : $"{directory}/{fallbackName}";
    }

    private static string GetRemoteDirectory(string remotePath)
    {
        if (string.IsNullOrEmpty(remotePath) || remotePath == "/")
        {
            return "/";
        }

        var lastSlash = remotePath.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            return "/";
        }

        return remotePath[..lastSlash];
    }

    private List<FileItemViewModel> GetPermissionTargets()
    {
        var selected = RemoteBrowser.SelectedItems
            .Where(item => !item.IsParentDirectory)
            .ToList();

        if (selected.Count == 0 && RemoteBrowser.SelectedItem != null && !RemoteBrowser.SelectedItem.IsParentDirectory)
        {
            selected.Add(RemoteBrowser.SelectedItem);
        }

        return selected;
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

    private async Task StartDownloadTransferAsync(
        string localPath,
        string remotePath,
        long resumeOffset = 0,
        long? totalBytesOverride = null)
    {
        var fileName = Path.GetFileName(remotePath);

        var totalBytes = totalBytesOverride.HasValue && totalBytesOverride.Value > 0
            ? totalBytesOverride.Value
            : await GetRemoteFileSizeAsync(remotePath);

        var transferItem = new TransferItemViewModel
        {
            FileName = fileName,
            LocalPath = localPath,
            RemotePath = remotePath,
            Direction = TransferDirection.Download,
            TotalBytes = totalBytes,
            Status = TransferStatus.Pending,
            ResumeOffset = Math.Clamp(resumeOffset, 0, totalBytes)
        };

        InitializeTransferProgress(transferItem);
        EnqueueTransfer(transferItem);
    }

    private void InitializeTransferProgress(TransferItemViewModel transfer)
    {
        if (transfer.TotalBytes > 0 && transfer.ResumeOffset > 0)
        {
            transfer.TransferredBytes = transfer.ResumeOffset;
            transfer.Progress = transfer.ResumeOffset / (double)transfer.TotalBytes * 100.0;
        }
    }

    private void EnqueueTransfer(TransferItemViewModel transfer)
    {
        Transfers.Add(transfer);
        OnPropertyChanged(nameof(HasActiveTransfer));
        _ = ProcessTransferQueueAsync();
    }

    private async Task ProcessTransferQueueAsync()
    {
        if (_isProcessingQueue)
        {
            return;
        }

        _isProcessingQueue = true;
        try
        {
            while (true)
            {
                var next = Transfers.FirstOrDefault(t => t.Status == TransferStatus.Pending);
                if (next == null)
                {
                    break;
                }

                await ExecuteTransferAsync(next);
            }
        }
        finally
        {
            _isProcessingQueue = false;
        }
    }

    /// <summary>
    /// Cancels all active transfers.
    /// </summary>
    [RelayCommand]
    private void CancelAllTransfers()
    {
        foreach (var transfer in Transfers.Where(t => t.Status == TransferStatus.InProgress))
        {
            transfer.CancellationTokenSource?.Cancel();
            transfer.Status = TransferStatus.Cancelled;
        }

        foreach (var transfer in Transfers.Where(t => t.Status == TransferStatus.Pending))
        {
            transfer.Status = TransferStatus.Cancelled;
        }

        OnPropertyChanged(nameof(HasActiveTransfer));
    }

    /// <summary>
    /// Cancels a specific transfer.
    /// </summary>
    [RelayCommand]
    private void CancelTransfer(TransferItemViewModel? transfer)
    {
        if (transfer == null) return;

        transfer.CancellationTokenSource?.Cancel();
        transfer.Status = TransferStatus.Cancelled;
        OnPropertyChanged(nameof(HasActiveTransfer));
    }

    /// <summary>
    /// Retries a failed or cancelled transfer from the beginning.
    /// </summary>
    [RelayCommand]
    private void RetryTransfer(TransferItemViewModel? transfer)
    {
        if (transfer == null || transfer.Status == TransferStatus.InProgress)
        {
            return;
        }

        transfer.ErrorMessage = null;
        transfer.ResumeOffset = 0;
        transfer.CanResume = false;
        transfer.TransferredBytes = 0;
        transfer.Progress = 0;
        transfer.Status = TransferStatus.Pending;
        transfer.StartedAt = null;
        transfer.CompletedAt = null;
        OnPropertyChanged(nameof(HasActiveTransfer));
        _ = ProcessTransferQueueAsync();
    }

    /// <summary>
    /// Resumes a failed or cancelled transfer if possible.
    /// </summary>
    [RelayCommand]
    private async Task ResumeTransferAsync(TransferItemViewModel? transfer)
    {
        if (transfer == null || transfer.Status == TransferStatus.InProgress)
        {
            return;
        }

        await UpdateResumeStateAsync(transfer);
        if (!transfer.CanResume)
        {
            return;
        }

        transfer.ErrorMessage = null;
        transfer.Status = TransferStatus.Pending;
        transfer.StartedAt = null;
        transfer.CompletedAt = null;
        InitializeTransferProgress(transfer);
        OnPropertyChanged(nameof(HasActiveTransfer));
        _ = ProcessTransferQueueAsync();
    }

    /// <summary>
    /// Clears completed transfers from the list.
    /// </summary>
    [RelayCommand]
    private void ClearCompletedTransfers()
    {
        var completedTransfers = Transfers
            .Where(t => t.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled)
            .ToList();

        foreach (var transfer in completedTransfers)
        {
            Transfers.Remove(transfer);
        }

        OnPropertyChanged(nameof(HasActiveTransfer));
    }

    /// <summary>
    /// Disconnects the SFTP session.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        CancelAllTransfers();
        await _session.DisposeAsync();
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private async Task ExecuteTransferAsync(TransferItemViewModel transfer)
    {
        transfer.CancellationTokenSource = new CancellationTokenSource();
        var ct = transfer.CancellationTokenSource.Token;

        transfer.Status = TransferStatus.InProgress;
        transfer.StartedAt = DateTimeOffset.Now;
        if (transfer.TotalBytes > 0 && transfer.ResumeOffset > 0)
        {
            transfer.TransferredBytes = transfer.ResumeOffset;
            transfer.Progress = transfer.ResumeOffset / (double)transfer.TotalBytes * 100.0;
        }
        OnPropertyChanged(nameof(HasActiveTransfer));

        var progress = new Progress<double>(p =>
        {
            transfer.Progress = p;
            transfer.TransferredBytes = (long)(transfer.TotalBytes * p / 100.0);
        });

        try
        {
            if (transfer.Direction == TransferDirection.Upload)
            {
                await _session.UploadFileAsync(
                    transfer.LocalPath,
                    transfer.RemotePath,
                    progress,
                    ct,
                    transfer.ResumeOffset);
                _logger.LogInformation("Upload completed: {LocalPath} -> {RemotePath}", transfer.LocalPath, transfer.RemotePath);
                await RemoteBrowser.RefreshAsync();
            }
            else
            {
                await _session.DownloadFileAsync(
                    transfer.RemotePath,
                    transfer.LocalPath,
                    progress,
                    ct,
                    transfer.ResumeOffset);
                _logger.LogInformation("Download completed: {RemotePath} -> {LocalPath}", transfer.RemotePath, transfer.LocalPath);
                await LocalBrowser.RefreshAsync();
            }

            transfer.Status = TransferStatus.Completed;
            transfer.Progress = 100;
            transfer.TransferredBytes = transfer.TotalBytes;
            transfer.CanResume = false;
        }
        catch (OperationCanceledException)
        {
            transfer.Status = TransferStatus.Cancelled;
            _logger.LogInformation("Transfer cancelled: {FileName}", transfer.FileName);
            await UpdateResumeStateAsync(transfer);
        }
        catch (Exception ex)
        {
            transfer.Status = TransferStatus.Failed;
            transfer.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Transfer failed: {FileName}", transfer.FileName);
            await UpdateResumeStateAsync(transfer);
        }
        finally
        {
            transfer.CompletedAt = DateTimeOffset.Now;
            OnPropertyChanged(nameof(HasActiveTransfer));

            // Schedule auto-removal for completed transfers
            if (transfer.Status == TransferStatus.Completed)
            {
                _ = ScheduleTransferRemovalAsync(transfer);
            }
        }
    }

    private async Task UpdateResumeStateAsync(TransferItemViewModel transfer)
    {
        if (transfer.TotalBytes <= 0)
        {
            transfer.CanResume = false;
            transfer.ResumeOffset = 0;
            return;
        }

        long existingSize = 0;
        try
        {
            if (transfer.Direction == TransferDirection.Upload)
            {
                var info = await _session.GetFileInfoAsync(transfer.RemotePath);
                existingSize = info?.Size ?? 0;
            }
            else
            {
                if (File.Exists(transfer.LocalPath))
                {
                    existingSize = new FileInfo(transfer.LocalPath).Length;
                }
            }
        }
        catch
        {
            existingSize = 0;
        }

        if (existingSize > 0 && existingSize < transfer.TotalBytes)
        {
            transfer.ResumeOffset = existingSize;
            transfer.CanResume = true;
            transfer.TransferredBytes = existingSize;
            transfer.Progress = existingSize / (double)transfer.TotalBytes * 100.0;
            return;
        }

        transfer.CanResume = false;
        transfer.ResumeOffset = 0;
    }

    /// <summary>
    /// Schedules automatic removal of a completed transfer after a delay.
    /// </summary>
    private async Task ScheduleTransferRemovalAsync(TransferItemViewModel transfer)
    {
        try
        {
            await Task.Delay(AutoRemoveDelayMs);

            // Only remove if still completed (not cancelled by user in the meantime)
            if (transfer.Status == TransferStatus.Completed && Transfers.Contains(transfer))
            {
                // Use dispatcher to ensure we're on the UI thread
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Transfers.Remove(transfer);
                    OnPropertyChanged(nameof(HasActiveTransfer));
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to auto-remove completed transfer");
        }
    }

    private void OnSessionDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        ErrorMessage = "SFTP session disconnected";
        _logger.LogWarning("SFTP session disconnected for {Hostname}", Hostname);
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoteBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RemoteBrowser.SelectedItem) ||
            e.PropertyName == nameof(RemoteBrowser.SelectedItems))
        {
            ShowPermissionsDialogCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        ShowPermissionsDialogCommand.NotifyCanExecuteChanged();
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

            var themeService = App.GetService<IEditorThemeService>();
            var viewModel = new TextEditorViewModel(themeService);

            // Get the SFTP session from the remote browser
            var session = RemoteBrowser.GetSession();
            if (session == null || !session.IsConnected)
            {
                ErrorMessage = "SFTP session is not connected";
                return;
            }

            // Load the remote file
            await viewModel.LoadRemoteFileAsync(session, item.FullPath, Hostname);

            // Show the editor window
            var editorWindow = new TextEditorWindow(viewModel, themeService)
            {
                Owner = ownerWindow
            };

            editorWindow.ShowDialog();

            // Refresh the remote browser in case the file was modified
            await RemoteBrowser.RefreshAsync();

            _logger.LogInformation("Closed editor for remote file: {Path}", item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open remote file for editing: {Path}", item.FullPath);
            ErrorMessage = $"Failed to open file: {ex.Message}";
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

            var themeService = App.GetService<IEditorThemeService>();
            var viewModel = new TextEditorViewModel(themeService);

            // Load the local file
            await viewModel.LoadLocalFileAsync(item.FullPath);

            // Show the editor window
            var editorWindow = new TextEditorWindow(viewModel, themeService)
            {
                Owner = ownerWindow
            };

            editorWindow.ShowDialog();

            // Refresh the local browser in case the file was modified
            await LocalBrowser.RefreshAsync();

            _logger.LogInformation("Closed editor for local file: {Path}", item.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open local file for editing: {Path}", item.FullPath);
            ErrorMessage = $"Failed to open file: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _session.Disconnected -= OnSessionDisconnected;
        RemoteBrowser.PropertyChanged -= OnRemoteBrowserPropertyChanged;
        CancelAllTransfers();
        await _session.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ViewModel for a file transfer operation with observable progress.
/// </summary>
public partial class TransferItemViewModel : ObservableObject
{
    /// <summary>
    /// Unique identifier for this transfer.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The name of the file being transferred.
    /// </summary>
    [ObservableProperty]
    private string _fileName = "";

    /// <summary>
    /// The local file path.
    /// </summary>
    [ObservableProperty]
    private string _localPath = "";

    /// <summary>
    /// The remote file path.
    /// </summary>
    [ObservableProperty]
    private string _remotePath = "";

    /// <summary>
    /// Direction of the transfer.
    /// </summary>
    [ObservableProperty]
    private TransferDirection _direction;

    /// <summary>
    /// Total size of the file in bytes.
    /// </summary>
    [ObservableProperty]
    private long _totalBytes;

    /// <summary>
    /// Current transfer status.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowRetryButton))]
    [NotifyPropertyChangedFor(nameof(ShowResumeButton))]
    private TransferStatus _status = TransferStatus.Pending;

    /// <summary>
    /// Number of bytes transferred so far.
    /// </summary>
    [ObservableProperty]
    private long _transferredBytes;

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private double _progress;

    /// <summary>
    /// Resume offset in bytes.
    /// </summary>
    [ObservableProperty]
    private long _resumeOffset;

    /// <summary>
    /// Whether the transfer can be resumed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResumeButton))]
    private bool _canResume;

    /// <summary>
    /// Error message if the transfer failed.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// When the transfer started.
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? _startedAt;

    /// <summary>
    /// When the transfer completed (or failed/cancelled).
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    /// <summary>
    /// Cancellation token source for this transfer.
    /// </summary>
    public CancellationTokenSource? CancellationTokenSource { get; set; }

    /// <summary>
    /// Direction display text.
    /// </summary>
    public string DirectionDisplay => Direction == TransferDirection.Upload ? "↑" : "↓";

    /// <summary>
    /// Status display text.
    /// </summary>
    public string StatusDisplay => Status switch
    {
        TransferStatus.Pending => "Queued",
        TransferStatus.InProgress => $"{Progress:N0}%",
        TransferStatus.Completed => "Done",
        TransferStatus.Failed => "Failed",
        TransferStatus.Cancelled => "Cancelled",
        _ => "Unknown"
    };

    public bool ShowCancelButton => Status == TransferStatus.InProgress;

    public bool ShowRetryButton => Status is TransferStatus.Failed or TransferStatus.Cancelled;

    public bool ShowResumeButton => ShowRetryButton && CanResume;
}
