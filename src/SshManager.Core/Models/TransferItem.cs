namespace SshManager.Core.Models;

/// <summary>
/// Represents an active or completed file transfer operation.
/// </summary>
public sealed class TransferItem
{
    /// <summary>
    /// Unique identifier for this transfer.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The name of the file being transferred.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The local file path.
    /// </summary>
    public required string LocalPath { get; init; }

    /// <summary>
    /// The remote file path.
    /// </summary>
    public required string RemotePath { get; init; }

    /// <summary>
    /// Direction of the transfer.
    /// </summary>
    public TransferDirection Direction { get; init; }

    /// <summary>
    /// Total size of the file in bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Current transfer status.
    /// </summary>
    public TransferStatus Status { get; set; } = TransferStatus.Pending;

    /// <summary>
    /// Number of bytes transferred so far.
    /// </summary>
    public long TransferredBytes { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double Progress => TotalBytes > 0 ? (double)TransferredBytes / TotalBytes * 100.0 : 0;

    /// <summary>
    /// Error message if the transfer failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When the transfer started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When the transfer completed (or failed/cancelled).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
