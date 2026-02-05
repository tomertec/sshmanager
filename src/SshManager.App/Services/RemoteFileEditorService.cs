using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SshManager.Terminal.Services;

namespace SshManager.App.Services;

/// <summary>
/// Service for editing remote files through SFTP.
/// Manages temporary files, tracks changes via SHA256 hashing, and handles upload on save.
/// </summary>
public sealed class RemoteFileEditorService : IRemoteFileEditorService, IDisposable
{
    private readonly ILogger<RemoteFileEditorService> _logger;
    private readonly ConcurrentDictionary<Guid, RemoteEditSession> _sessions = new();
    private readonly string _tempDirectory;
    private bool _disposed;

    public RemoteFileEditorService(ILogger<RemoteFileEditorService> logger)
    {
        _logger = logger;

        // Create a dedicated temp directory for remote file editing
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SshManager", "RemoteEdit");
        Directory.CreateDirectory(_tempDirectory);

        _logger.LogInformation("RemoteFileEditorService initialized. Temp directory: {TempDir}", _tempDirectory);
    }

    public IReadOnlyList<RemoteEditSession> ActiveSessions =>
        _sessions.Values.Where(s => !s.IsDisposed).ToList();

    public async Task<RemoteEditSession> OpenForEditingAsync(
        ISftpSession sftpSession,
        string remotePath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sftpSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);

        _logger.LogInformation("Opening remote file for editing: {RemotePath}", remotePath);

        // Check if we already have an active session for this path
        var existingSession = GetSessionForPath(remotePath);
        if (existingSession != null && !existingSession.IsDisposed)
        {
            _logger.LogWarning("Session already exists for path: {RemotePath}", remotePath);
            throw new InvalidOperationException($"A session already exists for '{remotePath}'. Close it first.");
        }

        // Download the file content
        var content = await sftpSession.ReadAllBytesAsync(remotePath, ct);
        _logger.LogDebug("Downloaded {Bytes} bytes from {RemotePath}", content.Length, remotePath);

        // Detect encoding (default to UTF-8)
        var encoding = FileEncodingHelper.DetectEncoding(content);

        // Compute hash of original content
        var originalHash = FileEncodingHelper.ComputeHash(content);

        // Create temp file with a unique name but preserving the original extension
        var sessionId = Guid.NewGuid();
        var fileName = Path.GetFileName(remotePath);
        var safeFileName = SanitizeFileName(fileName);
        var tempFileName = $"{sessionId:N}_{safeFileName}";
        var localTempPath = Path.Combine(_tempDirectory, tempFileName);

        // Write content to temp file
        await File.WriteAllBytesAsync(localTempPath, content, ct);
        _logger.LogDebug("Created temp file: {TempPath}", localTempPath);

        // Create the session
        var session = new RemoteEditSession
        {
            SessionId = sessionId,
            RemotePath = remotePath,
            LocalTempPath = localTempPath,
            OriginalHash = originalHash,
            SftpSession = sftpSession,
            StartedAt = DateTimeOffset.UtcNow,
            Encoding = encoding,
            OnDispose = async s => await CloseSessionAsync(s)
        };

        _sessions[sessionId] = session;

        _logger.LogInformation(
            "Editing session created. SessionId: {SessionId}, RemotePath: {RemotePath}",
            sessionId, remotePath);

        return session;
    }

    public async Task<string> ReadContentAsync(RemoteEditSession session, CancellationToken ct = default)
    {
        ValidateSession(session);

        var bytes = await File.ReadAllBytesAsync(session.LocalTempPath, ct);
        return session.Encoding.GetString(bytes);
    }

    public async Task WriteContentAsync(RemoteEditSession session, string content, CancellationToken ct = default)
    {
        ValidateSession(session);
        ArgumentNullException.ThrowIfNull(content);

        var bytes = session.Encoding.GetBytes(content);
        await File.WriteAllBytesAsync(session.LocalTempPath, bytes, ct);

        _logger.LogDebug("Written {Bytes} bytes to temp file for session {SessionId}",
            bytes.Length, session.SessionId);
    }

    public async Task<bool> HasChangesAsync(RemoteEditSession session, CancellationToken ct = default)
    {
        ValidateSession(session);

        var currentBytes = await File.ReadAllBytesAsync(session.LocalTempPath, ct);
        var currentHash = FileEncodingHelper.ComputeHash(currentBytes);

        return !string.Equals(session.OriginalHash, currentHash, StringComparison.Ordinal);
    }

    public async Task<SaveResult> SaveToRemoteAsync(RemoteEditSession session, bool forceSave = false, CancellationToken ct = default)
    {
        ValidateSession(session);

        try
        {
            // Read current local content
            var currentBytes = await File.ReadAllBytesAsync(session.LocalTempPath, ct);
            var currentHash = FileEncodingHelper.ComputeHash(currentBytes);

            var contentChanged = !string.Equals(session.OriginalHash, currentHash, StringComparison.Ordinal);

            if (!contentChanged)
            {
                _logger.LogInformation("No changes detected for {RemotePath}, skipping upload", session.RemotePath);
                return SaveResult.Succeeded(contentChanged: false, newHash: currentHash);
            }

            // Check if SFTP session is still connected
            if (!session.SftpSession.IsConnected)
            {
                _logger.LogError("SFTP session disconnected for {RemotePath}", session.RemotePath);
                return SaveResult.Failed("SFTP connection lost. Please reconnect and try again.");
            }

            // Verify remote file hasn't been modified since we opened it (unless force saving)
            if (!forceSave)
            {
                _logger.LogDebug("Checking for concurrent modifications to {RemotePath}", session.RemotePath);

                try
                {
                    var remoteBytes = await session.SftpSession.ReadAllBytesAsync(session.RemotePath, ct);
                    var remoteHash = FileEncodingHelper.ComputeHash(remoteBytes);

                    if (!string.Equals(session.OriginalHash, remoteHash, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "Conflict detected for {RemotePath}: remote file was modified since session opened. " +
                            "Original hash: {OriginalHash}, Remote hash: {RemoteHash}",
                            session.RemotePath, session.OriginalHash, remoteHash);

                        return SaveResult.Conflict(remoteHash);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // If we can't read the remote file (e.g., deleted), log warning but allow save attempt
                    _logger.LogWarning(ex,
                        "Could not verify remote file state for {RemotePath}, proceeding with save",
                        session.RemotePath);
                }
            }
            else
            {
                _logger.LogInformation("Force save requested, skipping conflict detection for {RemotePath}",
                    session.RemotePath);
            }

            // Upload to remote
            _logger.LogInformation("Uploading changes to {RemotePath} ({Bytes} bytes)",
                session.RemotePath, currentBytes.Length);

            await session.SftpSession.WriteAllBytesAsync(session.RemotePath, currentBytes, ct);

            _logger.LogInformation("Successfully saved changes to {RemotePath}", session.RemotePath);

            return SaveResult.Succeeded(contentChanged: true, newHash: currentHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save to remote: {RemotePath}", session.RemotePath);
            return SaveResult.Failed($"Failed to save: {ex.Message}");
        }
    }

    public async Task<string> ReloadFromRemoteAsync(RemoteEditSession session, CancellationToken ct = default)
    {
        ValidateSession(session);

        if (!session.SftpSession.IsConnected)
        {
            throw new InvalidOperationException("SFTP connection lost. Please reconnect.");
        }

        _logger.LogInformation("Reloading content from remote: {RemotePath}", session.RemotePath);

        // Download fresh content
        var content = await session.SftpSession.ReadAllBytesAsync(session.RemotePath, ct);

        // Write to temp file
        await File.WriteAllBytesAsync(session.LocalTempPath, content, ct);

        // Update OriginalHash so conflict detection works correctly on subsequent saves
        session.OriginalHash = FileEncodingHelper.ComputeHash(content);

        var text = session.Encoding.GetString(content);

        _logger.LogDebug("Reloaded {Bytes} bytes from {RemotePath}", content.Length, session.RemotePath);

        return text;
    }

    public Task CloseSessionAsync(RemoteEditSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_sessions.TryRemove(session.SessionId, out _))
        {
            _logger.LogDebug("Session {SessionId} already removed", session.SessionId);
            return Task.CompletedTask;
        }

        // Clean up temp file
        try
        {
            if (File.Exists(session.LocalTempPath))
            {
                File.Delete(session.LocalTempPath);
                _logger.LogDebug("Deleted temp file: {TempPath}", session.LocalTempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file: {TempPath}", session.LocalTempPath);
        }

        _logger.LogInformation("Closed editing session {SessionId} for {RemotePath}",
            session.SessionId, session.RemotePath);

        return Task.CompletedTask;
    }

    public Task CleanupAllAsync()
    {
        _logger.LogInformation("Cleaning up all editing sessions...");

        var sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
        {
            try
            {
                _sessions.TryRemove(session.SessionId, out _);

                if (File.Exists(session.LocalTempPath))
                {
                    File.Delete(session.LocalTempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up session {SessionId}", session.SessionId);
            }
        }

        // Also clean up any orphaned temp files in the directory
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                var orphanedFiles = Directory.GetFiles(_tempDirectory);
                foreach (var file in orphanedFiles)
                {
                    try
                    {
                        File.Delete(file);
                        _logger.LogDebug("Deleted orphaned temp file: {File}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete orphaned file: {File}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up temp directory");
        }

        _logger.LogInformation("Cleanup complete. Removed {Count} sessions.", sessions.Count);

        return Task.CompletedTask;
    }

    public RemoteEditSession? GetSessionForPath(string remotePath)
    {
        return _sessions.Values
            .FirstOrDefault(s => !s.IsDisposed &&
                                 string.Equals(s.RemotePath, remotePath, StringComparison.Ordinal));
    }

    private void ValidateSession(RemoteEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(RemoteEditSession),
                "The editing session has been disposed.");
        }

        if (!_sessions.ContainsKey(session.SessionId))
        {
            throw new InvalidOperationException(
                "The editing session is not registered with this service.");
        }

        if (!File.Exists(session.LocalTempPath))
        {
            throw new FileNotFoundException(
                "The temp file for this session no longer exists.",
                session.LocalTempPath);
        }
    }



    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName.Length);

        foreach (var c in fileName)
        {
            sanitized.Append(invalidChars.Contains(c) ? '_' : c);
        }

        // Ensure we have a valid file name
        var result = sanitized.ToString();
        if (string.IsNullOrWhiteSpace(result))
        {
            result = "unnamed";
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Synchronously clean up
        foreach (var session in _sessions.Values)
        {
            try
            {
                if (File.Exists(session.LocalTempPath))
                {
                    File.Delete(session.LocalTempPath);
                }
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        _sessions.Clear();
    }
}
