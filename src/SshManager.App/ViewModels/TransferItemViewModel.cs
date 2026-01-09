using CommunityToolkit.Mvvm.ComponentModel;

namespace SshManager.App.ViewModels;

/// <summary>
/// Direction of a file transfer operation.
/// </summary>
public enum TransferDirection
{
    Upload,
    Download
}

/// <summary>
/// Status of a file transfer operation.
/// </summary>
public enum TransferStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
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
    [NotifyPropertyChangedFor(nameof(SpeedDisplay))]
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

    /// <summary>
    /// Transfer speed display (e.g., "1.5 MB/s").
    /// </summary>
    public string SpeedDisplay
    {
        get
        {
            if (StartedAt == null || Status != TransferStatus.InProgress)
                return "";

            var elapsed = DateTimeOffset.Now - StartedAt.Value;
            if (elapsed.TotalSeconds < 0.5)
                return "";

            var bytesPerSecond = TransferredBytes / elapsed.TotalSeconds;
            return FormatSpeed(bytesPerSecond);
        }
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        string[] suffixes = ["B/s", "KB/s", "MB/s", "GB/s"];
        int suffixIndex = 0;
        double size = bytesPerSecond;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:N1} {suffixes[suffixIndex]}";
    }

    public bool ShowCancelButton => Status == TransferStatus.InProgress;

    public bool ShowRetryButton => Status is TransferStatus.Failed or TransferStatus.Cancelled;

    public bool ShowResumeButton => ShowRetryButton && CanResume;
}
