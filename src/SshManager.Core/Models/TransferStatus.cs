namespace SshManager.Core.Models;

/// <summary>
/// Status of a file transfer operation.
/// </summary>
public enum TransferStatus
{
    /// <summary>
    /// Transfer is waiting to start.
    /// </summary>
    Pending,

    /// <summary>
    /// Transfer is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Transfer completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Transfer failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Transfer was cancelled by the user.
    /// </summary>
    Cancelled
}
