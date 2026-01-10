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
public partial class SftpTransferManagerViewModel : ObservableObject
{
    private readonly ILogger<SftpTransferManagerViewModel> _logger;
    private readonly ISftpSession _session;
    private bool _isProcessingQueue;
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
            var fileName = Path.GetFileName(remotePath);
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
                totalBytes = new FileInfo(localPath).Length;
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
        var fileName = Path.GetFileName(remotePath);

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

    private void EnqueueTransfer(TransferItemViewModel transfer)
    {
        Transfers.Add(transfer);
        OnPropertyChanged(nameof(HasActiveTransfer));
        OnPropertyChanged(nameof(ActiveTransferCount));
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

                if (RefreshRemoteBrowserCallback != null)
                {
                    await RefreshRemoteBrowserCallback();
                }
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
        OnPropertyChanged(nameof(ActiveTransferCount));
    }

    /// <summary>
    /// Cancels a specific transfer.
    /// </summary>
    [RelayCommand]
    public void CancelTransfer(TransferItemViewModel? transfer)
    {
        if (transfer == null) return;

        transfer.CancellationTokenSource?.Cancel();
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
        _ = ProcessTransferQueueAsync();
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
        _ = ProcessTransferQueueAsync();
    }

    /// <summary>
    /// Clears completed transfers from the list.
    /// </summary>
    [RelayCommand]
    public void ClearCompletedTransfers()
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
        OnPropertyChanged(nameof(ActiveTransferCount));
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to auto-remove completed transfer");
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
}
