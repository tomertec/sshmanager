using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.Services;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// Main ViewModel for the SFTP file browser panel.
/// Composes local and remote file browsers and coordinates file operations, dialogs, and transfers.
/// </summary>
public partial class SftpBrowserViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly ISftpSession _session;

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
    /// Manages file transfer operations.
    /// </summary>
    [ObservableProperty]
    private SftpTransferManagerViewModel _transferManager;

    /// <summary>
    /// Manages dialog state and interactions.
    /// </summary>
    [ObservableProperty]
    private SftpDialogStateViewModel _dialogState;

    /// <summary>
    /// Manages file operations.
    /// </summary>
    [ObservableProperty]
    private SftpFileOperationsViewModel _fileOperations;

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
    /// Whether the local panel is collapsed.
    /// </summary>
    [ObservableProperty]
    private bool _isLocalPanelCollapsed;

    /// <summary>
    /// Whether the remote panel is collapsed.
    /// </summary>
    [ObservableProperty]
    private bool _isRemotePanelCollapsed;

    /// <summary>
    /// Whether mirror navigation mode is enabled (sync local and remote navigation).
    /// </summary>
    [ObservableProperty]
    private bool _isMirrorNavigationEnabled;

    /// <summary>
    /// Callback to get the mirror navigation setting from storage.
    /// </summary>
    private Func<bool>? _getMirrorNavigationCallback;

    /// <summary>
    /// Callback to save the mirror navigation setting to storage.
    /// </summary>
    private Action<bool>? _saveMirrorNavigationCallback;

    /// <summary>
    /// Semaphore to prevent recursive navigation syncing (async-safe).
    /// </summary>
    private readonly SemaphoreSlim _navigationSyncLock = new(1, 1);

    private bool _disposed;

    /// <summary>
    /// Prevents concurrent transfer batch operations that would conflict on the overwrite dialog.
    /// </summary>
    private readonly SemaphoreSlim _transferBatchLock = new(1, 1);

    /// <summary>
    /// TaskCompletionSource for awaiting the overwrite dialog result.
    /// </summary>
    private TaskCompletionSource<ConflictResolution?>? _overwriteTcs;

    // Facade properties for dialog state (for XAML binding compatibility)
    public bool IsNewFolderDialogVisible
    {
        get => DialogState.IsNewFolderDialogVisible;
        set
        {
            if (DialogState.IsNewFolderDialogVisible != value)
            {
                DialogState.IsNewFolderDialogVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewFolderName
    {
        get => DialogState.NewFolderName;
        set
        {
            if (DialogState.NewFolderName != value)
            {
                DialogState.NewFolderName = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsOverwriteDialogVisible
    {
        get => DialogState.IsOverwriteDialogVisible;
        set
        {
            if (DialogState.IsOverwriteDialogVisible != value)
            {
                DialogState.IsOverwriteDialogVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string OverwriteFileName
    {
        get => DialogState.OverwriteFileName;
        set
        {
            if (DialogState.OverwriteFileName != value)
            {
                DialogState.OverwriteFileName = value;
                OnPropertyChanged();
            }
        }
    }

    public bool OverwriteApplyToAll
    {
        get => DialogState.OverwriteApplyToAll;
        set
        {
            if (DialogState.OverwriteApplyToAll != value)
            {
                DialogState.OverwriteApplyToAll = value;
                OnPropertyChanged();
            }
        }
    }

    public string OverwriteSizeDisplay => DialogState.OverwriteSizeDisplay;

    public bool OverwriteCanResume => DialogState.OverwriteCanResume;

    public bool IsPermissionsDialogVisible
    {
        get => DialogState.IsPermissionsDialogVisible;
        set
        {
            if (DialogState.IsPermissionsDialogVisible != value)
            {
                DialogState.IsPermissionsDialogVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string PermissionsInput
    {
        get => DialogState.PermissionsInput;
        set
        {
            if (DialogState.PermissionsInput != value)
            {
                DialogState.PermissionsInput = value;
                OnPropertyChanged();
            }
        }
    }

    public string PermissionsTargetName => DialogState.PermissionsTargetName;

    public string PermissionsCurrentDisplay => DialogState.PermissionsCurrentDisplay;

    public string? PermissionsErrorMessage => DialogState.PermissionsErrorMessage;

    public bool IsDeleteDialogVisible
    {
        get => DialogState.IsDeleteDialogVisible;
        set
        {
            if (DialogState.IsDeleteDialogVisible != value)
            {
                DialogState.IsDeleteDialogVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string DeleteTargetName => DialogState.DeleteTargetName;

    public bool IsDeleteRemote => DialogState.IsDeleteRemote;

    public int DeleteItemCount => DialogState.DeleteItemCount;

    public bool IsDeleteDirectory => DialogState.IsDeleteDirectory;

    // Facade properties for transfers (for XAML binding compatibility)
    public ObservableCollection<TransferItemViewModel> Transfers => TransferManager.Transfers;

    public bool HasActiveTransfer => TransferManager.HasActiveTransfer;

    public int ActiveTransferCount => TransferManager.ActiveTransferCount;

    /// <summary>
    /// Event raised when the session is disconnected.
    /// </summary>
    public event EventHandler? Disconnected;

    public SftpBrowserViewModel(
        ISftpSession session,
        string hostname,
        IEditorThemeService editorThemeService,
        ILoggerFactory? loggerFactory = null)
    {
        _session = session;
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<SftpBrowserViewModel>();

        Hostname = hostname;
        IsConnected = session.IsConnected;

        // Initialize child view models
        _localBrowser = new LocalFileBrowserViewModel(factory.CreateLogger<LocalFileBrowserViewModel>());
        _remoteBrowser = new RemoteFileBrowserViewModel(factory.CreateLogger<RemoteFileBrowserViewModel>());
        _transferManager = new SftpTransferManagerViewModel(session, factory.CreateLogger<SftpTransferManagerViewModel>());
        _dialogState = new SftpDialogStateViewModel(session, factory.CreateLogger<SftpDialogStateViewModel>());
        _fileOperations = new SftpFileOperationsViewModel(session, hostname, editorThemeService, factory.CreateLogger<SftpFileOperationsViewModel>());

        // Set up the remote browser with the session
        _remoteBrowser.SetSession(session);
        _remoteBrowser.PropertyChanged += OnRemoteBrowserPropertyChanged;
        _localBrowser.PropertyChanged += OnLocalBrowserPropertyChanged;

        // Subscribe to session disconnect
        _session.Disconnected += OnSessionDisconnected;

        // Wire up callbacks for transfer manager
        _transferManager.GetRemoteFileSizeCallback = GetRemoteFileSizeAsync;
        _transferManager.GetUniqueRemotePathCallback = GetUniqueRemotePathAsync;
        _transferManager.RefreshRemoteBrowserCallback = () => RemoteBrowser.RefreshAsync();
        _transferManager.RefreshLocalBrowserCallback = () => LocalBrowser.RefreshAsync();

        // Wire up callbacks for dialog state
        _dialogState.RefreshLocalBrowserCallback = () => LocalBrowser.RefreshAsync();
        _dialogState.CreateRemoteDirectoryCallback = name => RemoteBrowser.CreateDirectoryAsync(name);
        _dialogState.GetCurrentLocalPathCallback = () => LocalBrowser.CurrentPath;
        _dialogState.GetRemoteErrorMessageCallback = () => RemoteBrowser.ErrorMessage;
        _dialogState.RefreshRemoteBrowserCallback = () => RemoteBrowser.RefreshAsync();
        _dialogState.SetErrorMessageAction = msg => ErrorMessage = msg;

        // Wire up callbacks for file operations
        _fileOperations.GetSelectedLocalItemCallback = () => LocalBrowser.SelectedItem;
        _fileOperations.GetSelectedRemoteItemCallback = () => RemoteBrowser.SelectedItem;
        _fileOperations.GetSelectedLocalItemsCallback = () => LocalBrowser.SelectedItems;
        _fileOperations.GetSelectedRemoteItemsCallback = () => RemoteBrowser.SelectedItems;
        _fileOperations.RefreshLocalBrowserCallback = () => LocalBrowser.RefreshAsync();
        _fileOperations.RefreshRemoteBrowserCallback = () => RemoteBrowser.RefreshAsync();
        _fileOperations.DeleteRemoteCallback = (item, recursive) => RemoteBrowser.DeleteAsync(item, recursive);
        _fileOperations.GetRemoteErrorMessageCallback = () => RemoteBrowser.ErrorMessage;
        _fileOperations.UploadFilesCallback = paths => UploadFiles(paths);
        _fileOperations.DownloadFilesCallback = paths => DownloadFiles(paths);
        _fileOperations.GetCurrentLocalPathCallback = () => LocalBrowser.CurrentPath;
        _fileOperations.GetRemoteBrowserSessionCallback = () => RemoteBrowser.GetSession();
        _fileOperations.SetErrorMessageAction = msg => ErrorMessage = msg;
        _fileOperations.ShowDeleteDialogCallback = (name, isRemote, count, isDir, action) =>
            DialogState.ShowDeleteDialog(name, isRemote, count, isDir, action);

        // Subscribe to dialog state property changes for facade updates
        _dialogState.PropertyChanged += OnDialogStatePropertyChanged;
        _transferManager.PropertyChanged += OnTransferManagerPropertyChanged;

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
    /// Toggles the local panel collapsed state.
    /// </summary>
    [RelayCommand]
    private void ToggleLocalPanel()
    {
        IsLocalPanelCollapsed = !IsLocalPanelCollapsed;
    }

    /// <summary>
    /// Toggles the remote panel collapsed state.
    /// </summary>
    [RelayCommand]
    private void ToggleRemotePanel()
    {
        IsRemotePanelCollapsed = !IsRemotePanelCollapsed;
    }

    /// <summary>
    /// Toggles mirror navigation mode.
    /// </summary>
    [RelayCommand]
    private void ToggleMirrorNavigation()
    {
        IsMirrorNavigationEnabled = !IsMirrorNavigationEnabled;
        _saveMirrorNavigationCallback?.Invoke(IsMirrorNavigationEnabled);
        _logger.LogInformation("Mirror navigation {State}", IsMirrorNavigationEnabled ? "enabled" : "disabled");
    }

    /// <summary>
    /// Sets up callbacks for settings persistence.
    /// </summary>
    public void SetSettingsCallbacks(
        Func<bool> getMirrorNavigation,
        Action<bool> saveMirrorNavigation,
        Func<string> getFavorites,
        Action<string> saveFavorites)
    {
        _getMirrorNavigationCallback = getMirrorNavigation;
        _saveMirrorNavigationCallback = saveMirrorNavigation;

        // Load initial value
        IsMirrorNavigationEnabled = getMirrorNavigation();

        // Set up favorites support for remote browser
        RemoteBrowser.SetFavoritesSupport(Hostname, getFavorites, saveFavorites);
    }

    // Facade commands for dialog state
    [RelayCommand]
    private void ShowNewLocalFolder() => DialogState.ShowNewLocalFolder();

    [RelayCommand]
    private void ShowNewRemoteFolder() => DialogState.ShowNewRemoteFolder();

    [RelayCommand]
    private async Task CreateNewFolderAsync() => await DialogState.CreateNewFolderAsync();

    [RelayCommand]
    private void CancelNewFolder() => DialogState.CancelNewFolder();

    [RelayCommand]
    private void ConfirmOverwrite() => CompleteOverwriteDialog(ConflictResolution.Overwrite);

    [RelayCommand]
    private void SkipOverwrite() => CompleteOverwriteDialog(ConflictResolution.Skip);

    [RelayCommand]
    private void ResumeOverwrite() => CompleteOverwriteDialog(ConflictResolution.Resume);

    [RelayCommand]
    private void KeepBothOverwrite() => CompleteOverwriteDialog(ConflictResolution.KeepBoth);

    [RelayCommand]
    private void CancelOverwrite() => CompleteOverwriteDialog(null);

    [RelayCommand(CanExecute = nameof(CanShowPermissionsDialog))]
    private void ShowPermissionsDialog()
    {
        var targets = GetPermissionTargets();
        if (targets.Count == 0)
        {
            return;
        }

        DialogState.ShowPermissionsDialog(targets);
    }

    private bool CanShowPermissionsDialog()
    {
        var item = RemoteBrowser.SelectedItem;
        return IsConnected && item != null && !item.IsParentDirectory;
    }

    [RelayCommand]
    private async Task ApplyPermissionsAsync() => await DialogState.ApplyPermissionsAsync();

    [RelayCommand]
    private void CancelPermissions() => DialogState.CancelPermissions();

    [RelayCommand]
    private async Task ConfirmDeleteAsync() => await DialogState.ConfirmDeleteAsync();

    [RelayCommand]
    private void CancelDelete() => DialogState.CancelDelete();

    // Facade commands for file operations
    [RelayCommand]
    private async Task DeleteLocalAsync() => await FileOperations.DeleteLocalAsync();

    [RelayCommand]
    private async Task DeleteRemoteAsync() => await FileOperations.DeleteRemoteAsync();

    [RelayCommand]
    private void UploadSelected() => FileOperations.UploadSelected();

    [RelayCommand]
    private void DownloadSelected() => FileOperations.DownloadSelected();

    // Facade commands for transfer manager
    [RelayCommand]
    private void CancelAllTransfers() => TransferManager.CancelAllTransfers();

    [RelayCommand]
    private void CancelTransfer(TransferItemViewModel? transfer) => TransferManager.CancelTransfer(transfer);

    [RelayCommand]
    private void RetryTransfer(TransferItemViewModel? transfer) => TransferManager.RetryTransfer(transfer);

    [RelayCommand]
    private async Task ResumeTransferAsync(TransferItemViewModel? transfer) => await TransferManager.ResumeTransferAsync(transfer);

    [RelayCommand]
    private void ClearCompletedTransfers() => TransferManager.ClearCompletedTransfers();

    /// <summary>
    /// Uploads files from local paths to the current remote directory.
    /// </summary>
    public void UploadFiles(IReadOnlyList<string> localPaths)
    {
        _ = UploadFilesGuardedAsync(localPaths).ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Upload error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task UploadFilesGuardedAsync(IReadOnlyList<string> localPaths)
    {
        await _transferBatchLock.WaitAsync();
        try
        {
            await TransferManager.UploadFilesAsync(
                localPaths,
                RemoteBrowser.CurrentPath,
                (lp, rp, es, ts, cr) => ShowOverwriteConflictAsync(lp, rp, es, ts, cr, isUpload: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload operation failed unexpectedly");
        }
        finally
        {
            _transferBatchLock.Release();
        }
    }

    /// <summary>
    /// Downloads files from remote paths to the current local directory.
    /// </summary>
    public void DownloadFiles(IReadOnlyList<string> remotePaths)
    {
        _ = DownloadFilesGuardedAsync(remotePaths).ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Download error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task DownloadFilesGuardedAsync(IReadOnlyList<string> remotePaths)
    {
        await _transferBatchLock.WaitAsync();
        try
        {
            await TransferManager.DownloadFilesAsync(
                remotePaths,
                LocalBrowser.CurrentPath,
                (lp, rp, es, ts, cr) => ShowOverwriteConflictAsync(lp, rp, es, ts, cr, isUpload: false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download operation failed unexpectedly");
        }
        finally
        {
            _transferBatchLock.Release();
        }
    }

    private Task<ConflictResolution?> ShowOverwriteConflictAsync(
        string localPath,
        string remotePath,
        long existingSize,
        long totalSize,
        bool canResume,
        bool isUpload)
    {
        // Cancel any previous pending dialog to prevent orphaned tasks
        _overwriteTcs?.TrySetResult(null);
        _overwriteTcs = new TaskCompletionSource<ConflictResolution?>();

        var fileName = System.IO.Path.GetFileName(localPath);
        DialogState.ShowOverwriteDialog(fileName, isUpload: isUpload, existingSize, totalSize, canResume);

        return _overwriteTcs.Task;
    }

    /// <summary>
    /// Completes the overwrite dialog with the given resolution.
    /// </summary>
    private void CompleteOverwriteDialog(ConflictResolution? resolution)
    {
        DialogState.HideOverwriteDialog();

        if (OverwriteApplyToAll && resolution.HasValue)
        {
            TransferManager.SetApplyToAllResolution(resolution.Value);
        }

        _overwriteTcs?.TrySetResult(resolution);
        _overwriteTcs = null;
    }

    /// <summary>
    /// Opens a remote file in the text editor.
    /// </summary>
    public async Task EditRemoteFileAsync(FileItemViewModel item, System.Windows.Window ownerWindow)
    {
        await FileOperations.EditRemoteFileAsync(item, ownerWindow);
    }

    /// <summary>
    /// Opens a local file in the text editor.
    /// </summary>
    public async Task EditLocalFileAsync(FileItemViewModel item, System.Windows.Window ownerWindow)
    {
        await FileOperations.EditLocalFileAsync(item, ownerWindow);
    }

    /// <summary>
    /// Disconnects the SFTP session.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected) return;

        _session.Disconnected -= OnSessionDisconnected;
        TransferManager.CancelAllTransfers();
        await _session.DisposeAsync();
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
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

    private async Task<string> GetUniqueRemotePathAsync(string remotePath)
    {
        var directory = GetRemoteDirectory(remotePath);
        var name = System.IO.Path.GetFileNameWithoutExtension(remotePath);
        var extension = System.IO.Path.GetExtension(remotePath);

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

        // Handle mirror navigation
        if (e.PropertyName == nameof(RemoteBrowser.CurrentPath) && IsMirrorNavigationEnabled)
        {
            _ = SyncLocalToRemotePath().ContinueWith(t =>
                _logger.LogDebug(t.Exception, "Mirror navigation sync failed"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void OnLocalBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Handle mirror navigation
        if (e.PropertyName == nameof(LocalBrowser.CurrentPath) && IsMirrorNavigationEnabled)
        {
            _ = SyncRemoteToLocalPath().ContinueWith(t =>
                _logger.LogDebug(t.Exception, "Mirror navigation sync failed"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    /// <summary>
    /// Syncs the local browser path to match the remote path structure.
    /// For mirror navigation: when remote changes, try to navigate local to equivalent path.
    /// </summary>
    private async Task SyncLocalToRemotePath()
    {
        if (!await _navigationSyncLock.WaitAsync(0)) return;

        try
        {
            // Get the relative path from home directory on remote
            var remotePath = RemoteBrowser.CurrentPath;
            var remoteHome = RemoteBrowser.HomeDirectory;

            string relativePath;
            if (remotePath.StartsWith(remoteHome) && remotePath.Length > remoteHome.Length)
            {
                relativePath = remotePath[remoteHome.Length..].TrimStart('/');
            }
            else
            {
                // If not under home, just use the folder name
                var lastSlash = remotePath.LastIndexOf('/');
                relativePath = lastSlash >= 0 ? remotePath[(lastSlash + 1)..] : remotePath;
            }

            if (string.IsNullOrEmpty(relativePath)) return;

            // Try to navigate to equivalent local path
            var localHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var targetPath = System.IO.Path.Combine(localHome, relativePath.Replace('/', '\\'));

            if (System.IO.Directory.Exists(targetPath))
            {
                try
                {
                    await LocalBrowser.NavigateToAsync(targetPath);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Mirror navigation to local path failed");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Mirror navigation sync failed (local to remote)");
        }
        finally
        {
            _navigationSyncLock.Release();
        }
    }

    /// <summary>
    /// Syncs the remote browser path to match the local path structure.
    /// For mirror navigation: when local changes, try to navigate remote to equivalent path.
    /// </summary>
    private async Task SyncRemoteToLocalPath()
    {
        if (!await _navigationSyncLock.WaitAsync(0)) return;

        try
        {
            // Get the relative path from user profile on local
            var localPath = LocalBrowser.CurrentPath;
            var localHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string relativePath;
            if (localPath.StartsWith(localHome, StringComparison.OrdinalIgnoreCase) && localPath.Length > localHome.Length)
            {
                relativePath = localPath[localHome.Length..].TrimStart('\\');
            }
            else
            {
                // If not under home, just use the folder name
                relativePath = System.IO.Path.GetFileName(localPath);
            }

            if (string.IsNullOrEmpty(relativePath)) return;

            // Try to navigate to equivalent remote path
            var remoteHome = RemoteBrowser.HomeDirectory;
            var targetPath = remoteHome == "/" ? "/" + relativePath.Replace('\\', '/') : remoteHome + "/" + relativePath.Replace('\\', '/');

            try
            {
                await RemoteBrowser.NavigateToAsync(targetPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Mirror navigation to remote path failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Mirror navigation sync failed (remote to local)");
        }
        finally
        {
            _navigationSyncLock.Release();
        }
    }

    private void OnDialogStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward relevant property changes for facade properties
        switch (e.PropertyName)
        {
            case nameof(DialogState.IsNewFolderDialogVisible):
                OnPropertyChanged(nameof(IsNewFolderDialogVisible));
                break;
            case nameof(DialogState.NewFolderName):
                OnPropertyChanged(nameof(NewFolderName));
                break;
            case nameof(DialogState.IsOverwriteDialogVisible):
                OnPropertyChanged(nameof(IsOverwriteDialogVisible));
                break;
            case nameof(DialogState.OverwriteFileName):
                OnPropertyChanged(nameof(OverwriteFileName));
                break;
            case nameof(DialogState.OverwriteApplyToAll):
                OnPropertyChanged(nameof(OverwriteApplyToAll));
                break;
            case nameof(DialogState.OverwriteSizeDisplay):
                OnPropertyChanged(nameof(OverwriteSizeDisplay));
                break;
            case nameof(DialogState.OverwriteCanResume):
                OnPropertyChanged(nameof(OverwriteCanResume));
                break;
            case nameof(DialogState.IsPermissionsDialogVisible):
                OnPropertyChanged(nameof(IsPermissionsDialogVisible));
                break;
            case nameof(DialogState.PermissionsInput):
                OnPropertyChanged(nameof(PermissionsInput));
                break;
            case nameof(DialogState.PermissionsTargetName):
                OnPropertyChanged(nameof(PermissionsTargetName));
                break;
            case nameof(DialogState.PermissionsCurrentDisplay):
                OnPropertyChanged(nameof(PermissionsCurrentDisplay));
                break;
            case nameof(DialogState.PermissionsErrorMessage):
                OnPropertyChanged(nameof(PermissionsErrorMessage));
                break;
            case nameof(DialogState.IsDeleteDialogVisible):
                OnPropertyChanged(nameof(IsDeleteDialogVisible));
                break;
            case nameof(DialogState.DeleteTargetName):
                OnPropertyChanged(nameof(DeleteTargetName));
                break;
            case nameof(DialogState.IsDeleteRemote):
                OnPropertyChanged(nameof(IsDeleteRemote));
                break;
            case nameof(DialogState.DeleteItemCount):
                OnPropertyChanged(nameof(DeleteItemCount));
                break;
            case nameof(DialogState.IsDeleteDirectory):
                OnPropertyChanged(nameof(IsDeleteDirectory));
                break;
        }
    }

    private void OnTransferManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward relevant property changes for facade properties
        switch (e.PropertyName)
        {
            case nameof(TransferManager.HasActiveTransfer):
                OnPropertyChanged(nameof(HasActiveTransfer));
                break;
            case nameof(TransferManager.ActiveTransferCount):
                OnPropertyChanged(nameof(ActiveTransferCount));
                break;
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        ShowPermissionsDialogCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _session.Disconnected -= OnSessionDisconnected;
        RemoteBrowser.PropertyChanged -= OnRemoteBrowserPropertyChanged;
        LocalBrowser.PropertyChanged -= OnLocalBrowserPropertyChanged;
        DialogState.PropertyChanged -= OnDialogStatePropertyChanged;
        TransferManager.PropertyChanged -= OnTransferManagerPropertyChanged;
        TransferManager.CancelAllTransfers();
        TransferManager.Dispose();
        _navigationSyncLock.Dispose();
        _transferBatchLock.Dispose();

        await _session.DisposeAsync();
        IsConnected = false;
    }
}
