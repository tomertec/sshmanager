using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// Conflict resolution strategy for file transfers.
/// </summary>
public enum ConflictResolution
{
    Overwrite,
    Skip,
    Resume,
    KeepBoth
}

/// <summary>
/// Manages file transfer operations, queueing, and progress tracking.
/// </summary>
public partial class SftpTransferManagerViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<SftpTransferManagerViewModel> _logger;
    private readonly ISftpSession _session;
    private readonly SemaphoreSlim _queueSemaphore = new(1, 1);
    private readonly object _transfersLock = new();
    private ConflictResolution? _applyConflictResolution;

    /// <summary>
    /// Delay in milliseconds before auto-removing completed transfers.
    /// </summary>
    private const int AutoRemoveDelayMs = 5000;

    /// <summary>
    /// Active and recent transfers.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _transfers = [];

    /// <summary>
    /// Whether a transfer is currently in progress.
    /// </summary>
    public bool HasActiveTransfer => Transfers.Any(t =>
        t.Status == TransferStatus.InProgress || t.Status == TransferStatus.Pending);

    /// <summary>
    /// Count of active (pending or in-progress) transfers.
    /// </summary>
    public int ActiveTransferCount => Transfers.Count(t =>
        t.Status == TransferStatus.InProgress || t.Status == TransferStatus.Pending);

    /// <summary>
    /// Callback to get the remote file size.
    /// </summary>
    public Func<string, Task<long>>? GetRemoteFileSizeCallback { get; set; }

    /// <summary>
    /// Callback to get a unique remote path.
    /// </summary>
    public Func<string, Task<string>>? GetUniqueRemotePathCallback { get; set; }

    /// <summary>
    /// Callback to refresh the remote browser.
    /// </summary>
    public Func<Task>? RefreshRemoteBrowserCallback { get; set; }

    /// <summary>
    /// Callback to refresh the local browser.
    /// </summary>
    public Func<Task>? RefreshLocalBrowserCallback { get; set; }

    public SftpTransferManagerViewModel(
        ISftpSession session,
        ILogger<SftpTransferManagerViewModel>? logger = null)
    {
        _session = session;
        _logger = logger ?? NullLogger<SftpTransferManagerViewModel>.Instance;
        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_transfers, _transfersLock);
    }

    /// <summary>
    /// Uploads files from local paths to the remote directory.
    /// </summary>
    public async Task UploadFilesAsync(
        IReadOnlyList<string> localPaths,
        string remoteBasePath,
        Func<string, string, long, long, bool, Task<ConflictResolution?>> conflictResolver)
    {
        var transfers = new List<(string LocalPath, string RemotePath)>();

        // Build list of transfers with remote paths
        foreach (var localPath in localPaths)
        {
            var fileName = Path.GetFileName(localPath);
            var remotePath = remoteBasePath == "/"
                ? "/" + fileName
                : remoteBasePath + "/" + fileName;

            transfers.Add((localPath, remotePath));
        }

        // Process each transfer with conflict resolution
        await ProcessTransfersAsync(transfers, isUpload: true, conflictResolver);
    }

    /// <summary>
    /// Downloads files from remote paths to the local directory.
    /// </summary>
    public async Task DownloadFilesAsync(
        IReadOnlyList<string> remotePaths,
        string localBasePath,
        Func<string, string, long, long, bool, Task<ConflictResolution?>> conflictResolver)
    {
        var transfers = new List<(string LocalPath, string RemotePath)>();

        // Build list of transfers with local paths
        foreach (var remotePath in remotePaths)
        {
            var fileName = GetRemoteFileName(remotePath);
            var localPath = Path.Combine(localBasePath, fileName);

            transfers.Add((localPath, remotePath));
        }

        // Process each transfer with conflict resolution
        await ProcessTransfersAsync(transfers, isUpload: false, conflictResolver);
    }

    private async Task ProcessTransfersAsync(
        List<(string LocalPath, string RemotePath)> transfers,
        bool isUpload,
        Func<string, string, long, long, bool, Task<ConflictResolution?>> conflictResolver)
    {
        _applyConflictResolution = null;

        foreach (var (localPath, remotePath) in transfers)
        {
            // Check for existing files
            long existingSize = 0;
            long totalBytes = 0;
            bool exists = false;

            if (isUpload)
            {
                try
                {
                    totalBytes = new FileInfo(localPath).Length;
                }
                catch (Exception ex) when (ex is FileNotFoundException or IOException)
                {
                    _logger.LogWarning(ex, "Source file not accessible: {Path}", localPath);
                    continue;
                }
                try
                {
                    var existingInfo = await _session.GetFileInfoAsync(remotePath);
                    exists = existingInfo != null;
                    existingSize = existingInfo?.Size ?? 0;
                }
                catch
                {
                    // File doesn't exist
                }
            }
            else
            {
                exists = File.Exists(localPath);
                existingSize = exists ? new FileInfo(localPath).Length : 0;
                totalBytes = GetRemoteFileSizeCallback != null
                    ? await GetRemoteFileSizeCallback(remotePath)
                    : 0;
            }

            // Handle conflict resolution
            if (exists)
            {
                ConflictResolution resolution;

                if (_applyConflictResolution.HasValue)
                {
                    resolution = _applyConflictResolution.Value;
                }
                else
                {
                    var canResume = existingSize > 0 && totalBytes > 0 && existingSize < totalBytes;
                    var result = await conflictResolver(localPath, remotePath, existingSize, totalBytes, canResume);

                    if (!result.HasValue)
                    {
                        // User cancelled
                        return;
                    }

                    resolution = result.Value;
                }

                await ApplyConflictResolutionAsync(
                    resolution,
                    localPath,
                    remotePath,
                    isUpload,
                    existingSize,
                    totalBytes);
            }
            else
            {
                // Start the transfer
                if (isUpload)
                {
                    StartUploadTransfer(localPath, remotePath);
                }
                else
                {
                    await StartDownloadTransferAsync(localPath, remotePath, 0, totalBytes);
                }
            }
        }

        _applyConflictResolution = null;
    }

    /// <summary>
    /// Sets the conflict resolution to apply to all remaining transfers.
    /// </summary>
    public void SetApplyToAllResolution(ConflictResolution resolution)
    {
        _applyConflictResolution = resolution;
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
                    var uniqueRemotePath = GetUniqueRemotePathCallback != null
                        ? await GetUniqueRemotePathCallback(remotePath)
                        : remotePath;
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

    private async Task StartDownloadTransferAsync(
        string localPath,
        string remotePath,
        long resumeOffset = 0,
        long? totalBytesOverride = null)
    {
        var fileName = GetRemoteFileName(remotePath);

        var totalBytes = totalBytesOverride.HasValue && totalBytesOverride.Value > 0
            ? totalBytesOverride.Value
            : (GetRemoteFileSizeCallback != null ? await GetRemoteFileSizeCallback(remotePath) : 0);

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

    /// <summary>
    /// Executes an action on the UI thread if needed.
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void EnqueueTransfer(TransferItemViewModel transfer)
    {
        RunOnUiThread(() =>
        {
            Transfers.Add(transfer);
            OnPropertyChanged(nameof(HasActiveTransfer));
            OnPropertyChanged(nameof(ActiveTransferCount));
        });
        _ = ProcessTransferQueueAsync()
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Transfer queue processing failed unexpectedly");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task ProcessTransferQueueAsync()
    {
        if (!await _queueSemaphore.WaitAsync(0))
        {
            return;
        }

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
            _queueSemaphore.Release();
        }
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
        OnPropertyChanged(nameof(ActiveTransferCount));

        // IMPORTANT: Progress<T> must be created on the UI thread so that its
        // SynchronizationContext captures the dispatcher for callback marshaling.
        var progress = new Progress<TransferProgress>(p =>
        {
            transfer.Progress = p.PercentComplete;
            transfer.TransferredBytes = p.BytesTransferred;
        });

        try
        {
            if (transfer.Direction == TransferDirection.Upload)
            {
                await _session.UploadFileWithStatsAsync(
                    transfer.LocalPath,
                    transfer.RemotePath,
                    progress,
                    ct,
                    transfer.ResumeOffset);
                _logger.LogInformation("Upload completed: {LocalPath} -> {RemotePath}", transfer.LocalPath, transfer.RemotePath);

                if (RefreshRemoteBrowserCallback != null)
                {
                    await RefreshRemoteBrowserCallback();
                }
            }
            else
            {
                await _session.DownloadFileWithStatsAsync(
                    transfer.RemotePath,
                    transfer.LocalPath,
                    progress,
                    ct,
                    transfer.ResumeOffset);
                _logger.LogInformation("Download completed: {RemotePath} -> {LocalPath}", transfer.RemotePath, transfer.LocalPath);

                if (RefreshLocalBrowserCallback != null)
                {
                    await RefreshLocalBrowserCallback();
                }
            }

            transfer.Status = TransferStatus.Completed;
            transfer.Progress = 100;
            transfer.TransferredBytes = transfer.TotalBytes;
            transfer.CanResume = false;
        }
        catch (OperationCanceledException)
        {
            if (transfer.Status != TransferStatus.Cancelled)
                transfer.Status = TransferStatus.Cancelled;
            _logger.LogInformation("Transfer cancelled: {FileName}", transfer.FileName);
            await UpdateResumeStateAsync(transfer);
        }
        catch (Exception ex)
        {
            if (transfer.Status != TransferStatus.Cancelled)
            {
                transfer.Status = TransferStatus.Failed;
                transfer.ErrorMessage = ex.Message;
            }
            _logger.LogError(ex, "Transfer failed: {FileName}", transfer.FileName);
            await UpdateResumeStateAsync(transfer);
        }
        finally
        {
            transfer.CompletedAt = DateTimeOffset.Now;

            // Dispose the CancellationTokenSource and null it out to prevent
            // ObjectDisposedException if CancelTransfer/CancelAllTransfers is called later
            var cts = transfer.CancellationTokenSource;
            transfer.CancellationTokenSource = null;
            cts?.Dispose();

            OnPropertyChanged(nameof(HasActiveTransfer));
            OnPropertyChanged(nameof(ActiveTransferCount));

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
    /// Cancels all active transfers.
    /// </summary>
    [RelayCommand]
    public void CancelAllTransfers()
    {
        foreach (var transfer in Transfers.Where(t => t.Status == TransferStatus.InProgress).ToList())
        {
            try { transfer.CancellationTokenSource?.Cancel(); } catch (ObjectDisposedException)
            {
                // Expected: CancellationTokenSource may already be disposed
                // when transfers complete or are cancelled concurrently
            }
            transfer.Status = TransferStatus.Cancelled;
        }

        foreach (var transfer in Transfers.Where(t => t.Status == TransferStatus.Pending).ToList())
        {
            transfer.Status = TransferStatus.Cancelled;
        }

        OnPropertyChanged(nameof(HasActiveTransfer));
        OnPropertyChanged(nameof(ActiveTransferCount));
    }

    /// <summary>
    /// Cancels a specific transfer.
    /// </summary>
    [RelayCommand]
    public void CancelTransfer(TransferItemViewModel? transfer)
    {
        if (transfer == null) return;

        try { transfer.CancellationTokenSource?.Cancel(); } catch (ObjectDisposedException)
        {
            // Expected: CancellationTokenSource may already be disposed
            // when transfers complete or are cancelled concurrently
        }
        transfer.Status = TransferStatus.Cancelled;
        OnPropertyChanged(nameof(HasActiveTransfer));
        OnPropertyChanged(nameof(ActiveTransferCount));
    }

    /// <summary>
    /// Retries a failed or cancelled transfer from the beginning.
    /// </summary>
    [RelayCommand]
    public void RetryTransfer(TransferItemViewModel? transfer)
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
        OnPropertyChanged(nameof(ActiveTransferCount));
        _ = ProcessTransferQueueAsync()
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Transfer queue processing failed unexpectedly");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Resumes a failed or cancelled transfer if possible.
    /// </summary>
    [RelayCommand]
    public async Task ResumeTransferAsync(TransferItemViewModel? transfer)
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
        OnPropertyChanged(nameof(ActiveTransferCount));
        _ = ProcessTransferQueueAsync()
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Transfer queue processing failed unexpectedly");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Clears completed transfers from the list.
    /// </summary>
    [RelayCommand]
    public void ClearCompletedTransfers()
    {
        RunOnUiThread(() =>
        {
            var completedTransfers = Transfers
                .Where(t => t.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled)
                .ToList();

            foreach (var transfer in completedTransfers)
            {
                Transfers.Remove(transfer);
            }

            OnPropertyChanged(nameof(HasActiveTransfer));
            OnPropertyChanged(nameof(ActiveTransferCount));
        });
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
                RunOnUiThread(() =>
                {
                    Transfers.Remove(transfer);
                    OnPropertyChanged(nameof(HasActiveTransfer));
                    OnPropertyChanged(nameof(ActiveTransferCount));
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to auto-remove completed transfer");
        }
    }

    public void Dispose()
    {
        _queueSemaphore.Dispose();
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

    /// <summary>
    /// Gets the file name from a remote Unix path (last segment after '/').
    /// Unlike Path.GetFileName, this correctly handles Unix paths on Windows.
    /// </summary>
    private static string GetRemoteFileName(string remotePath)
    {
        if (string.IsNullOrEmpty(remotePath)) return "";
        var lastSlash = remotePath.LastIndexOf('/');
        return lastSlash >= 0 ? remotePath[(lastSlash + 1)..] : remotePath;
    }
}
