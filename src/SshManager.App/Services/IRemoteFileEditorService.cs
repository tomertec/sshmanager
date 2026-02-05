using SshManager.Terminal.Services;

namespace SshManager.App.Services;

/// <summary>
/// Represents an active remote file editing session.
/// </summary>
public sealed class RemoteEditSession : IAsyncDisposable
{
    /// <summary>
    /// Unique identifier for this editing session.
    /// </summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// The full remote path of the file being edited.
    /// </summary>
    public required string RemotePath { get; init; }

    /// <summary>
    /// The local temporary file path where the content is stored.
    /// </summary>
    public required string LocalTempPath { get; init; }

    /// <summary>
    /// SHA256 hash of the original content when downloaded.
    /// Used for change detection.
    /// </summary>
    public required string OriginalHash { get; set; }

    /// <summary>
    /// The SFTP session used for this editing session.
    /// </summary>
    public required ISftpSession SftpSession { get; init; }

    /// <summary>
    /// Timestamp when the editing session started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// The file encoding detected or used.
    /// </summary>
    public required System.Text.Encoding Encoding { get; init; }

    /// <summary>
    /// Whether this session has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Action to call when disposing this session.
    /// </summary>
    internal Func<RemoteEditSession, ValueTask>? OnDispose { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        if (OnDispose != null)
        {
            await OnDispose(this);
        }
    }
}

/// <summary>
/// Result of a save operation.
/// </summary>
public sealed class SaveResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ContentChanged { get; init; }
    public string? NewHash { get; init; }

    /// <summary>
    /// True if the remote file was modified since it was opened, causing a conflict.
    /// When true, the save was aborted to prevent overwriting concurrent changes.
    /// </summary>
    public bool HasConflict { get; init; }

    /// <summary>
    /// The current hash of the remote file when a conflict is detected.
    /// </summary>
    public string? RemoteHash { get; init; }

    public static SaveResult Succeeded(bool contentChanged, string newHash) =>
        new() { Success = true, ContentChanged = contentChanged, NewHash = newHash };

    public static SaveResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };

    public static SaveResult Conflict(string remoteHash) =>
        new()
        {
            Success = false,
            HasConflict = true,
            RemoteHash = remoteHash,
            ErrorMessage = "The remote file was modified by another process. Reload to see changes or force save to overwrite."
        };
}

/// <summary>
/// Service for editing remote files through SFTP.
/// Handles downloading to temp files, change detection, and uploading changes.
/// </summary>
public interface IRemoteFileEditorService
{
    /// <summary>
    /// Gets all currently active editing sessions.
    /// </summary>
    IReadOnlyList<RemoteEditSession> ActiveSessions { get; }

    /// <summary>
    /// Opens a remote file for editing by downloading it to a temporary local file.
    /// </summary>
    /// <param name="sftpSession">The SFTP session to use for file operations.</param>
    /// <param name="remotePath">The full path to the remote file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An editing session that tracks the file state.</returns>
    Task<RemoteEditSession> OpenForEditingAsync(
        ISftpSession sftpSession,
        string remotePath,
        CancellationToken ct = default);

    /// <summary>
    /// Reads the current content from the local temp file.
    /// </summary>
    /// <param name="session">The editing session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file content as a string.</returns>
    Task<string> ReadContentAsync(RemoteEditSession session, CancellationToken ct = default);

    /// <summary>
    /// Writes content to the local temp file.
    /// </summary>
    /// <param name="session">The editing session.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteContentAsync(RemoteEditSession session, string content, CancellationToken ct = default);

    /// <summary>
    /// Checks if the local temp file has been modified from the original content.
    /// </summary>
    /// <param name="session">The editing session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the content has changed.</returns>
    Task<bool> HasChangesAsync(RemoteEditSession session, CancellationToken ct = default);

    /// <summary>
    /// Saves the local changes back to the remote file.
    /// Only uploads if the content has actually changed.
    /// Verifies remote file hasn't been modified since opening to prevent lost updates.
    /// </summary>
    /// <param name="session">The editing session.</param>
    /// <param name="forceSave">If true, skip conflict detection and overwrite remote changes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success, whether changes were uploaded, or if a conflict was detected.</returns>
    Task<SaveResult> SaveToRemoteAsync(RemoteEditSession session, bool forceSave = false, CancellationToken ct = default);

    /// <summary>
    /// Reloads the content from the remote file, discarding local changes.
    /// </summary>
    /// <param name="session">The editing session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refreshed content.</returns>
    Task<string> ReloadFromRemoteAsync(RemoteEditSession session, CancellationToken ct = default);

    /// <summary>
    /// Closes an editing session and cleans up the temp file.
    /// </summary>
    /// <param name="session">The editing session to close.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CloseSessionAsync(RemoteEditSession session, CancellationToken ct = default);

    /// <summary>
    /// Cleans up all temp files and sessions. Called during app shutdown.
    /// </summary>
    Task CleanupAllAsync();

    /// <summary>
    /// Gets an existing session for a remote path if one exists.
    /// </summary>
    /// <param name="remotePath">The remote file path.</param>
    /// <returns>The existing session or null.</returns>
    RemoteEditSession? GetSessionForPath(string remotePath);
}
