namespace SshManager.Core.Models;

/// <summary>
/// Direction of a file transfer operation.
/// </summary>
public enum TransferDirection
{
    /// <summary>
    /// Uploading a file from local to remote.
    /// </summary>
    Upload,

    /// <summary>
    /// Downloading a file from remote to local.
    /// </summary>
    Download
}
