using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for establishing SFTP connections.
/// </summary>
public interface ISftpService
{
    /// <summary>
    /// Connects to an SFTP server using the provided connection information.
    /// </summary>
    /// <param name="connectionInfo">The connection parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An SFTP session for file operations.</returns>
    Task<ISftpSession> ConnectAsync(TerminalConnectionInfo connectionInfo, CancellationToken ct = default);
}

/// <summary>
/// Represents an active SFTP session for file operations.
/// </summary>
public interface ISftpSession : IAsyncDisposable
{
    /// <summary>
    /// Whether the SFTP session is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the current working directory on the remote server.
    /// </summary>
    string WorkingDirectory { get; }

    /// <summary>
    /// Event raised when the session is disconnected.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Lists files and directories in the specified remote path.
    /// </summary>
    /// <param name="path">The remote directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of files and directories.</returns>
    Task<IReadOnlyList<SftpFileItem>> ListDirectoryAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Downloads a file from the remote server to the local filesystem.
    /// </summary>
    /// <param name="remotePath">The remote file path.</param>
    /// <param name="localPath">The local destination path.</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="ct">Cancellation token.</param>
    Task DownloadFileAsync(
        string remotePath,
        string localPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        long resumeOffset = 0);

    /// <summary>
    /// Uploads a file from the local filesystem to the remote server.
    /// </summary>
    /// <param name="localPath">The local file path.</param>
    /// <param name="remotePath">The remote destination path.</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="ct">Cancellation token.</param>
    Task UploadFileAsync(
        string localPath,
        string remotePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        long resumeOffset = 0);

    /// <summary>
    /// Creates a directory on the remote server.
    /// </summary>
    /// <param name="path">The remote directory path to create.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateDirectoryAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file or directory on the remote server.
    /// </summary>
    /// <param name="path">The remote path to delete.</param>
    /// <param name="recursive">If true and path is a directory, delete recursively.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default);

    /// <summary>
    /// Renames or moves a file or directory on the remote server.
    /// </summary>
    /// <param name="oldPath">The current path.</param>
    /// <param name="newPath">The new path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a file or directory exists on the remote server.
    /// </summary>
    /// <param name="path">The remote path to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the path exists.</returns>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Gets information about a specific file or directory.
    /// </summary>
    /// <param name="path">The remote path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>File information, or null if not found.</returns>
    Task<SftpFileItem?> GetFileInfoAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Changes permissions for a remote file or directory.
    /// </summary>
    /// <param name="path">The remote path.</param>
    /// <param name="permissions">Unix permissions (octal, e.g., 0755).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ChangePermissionsAsync(string path, int permissions, CancellationToken ct = default);

    /// <summary>
    /// Reads all bytes from a remote file.
    /// </summary>
    /// <param name="remotePath">The remote file path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file contents as a byte array.</returns>
    Task<byte[]> ReadAllBytesAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Writes all bytes to a remote file.
    /// </summary>
    /// <param name="remotePath">The remote file path.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteAllBytesAsync(string remotePath, byte[] content, CancellationToken ct = default);
}
