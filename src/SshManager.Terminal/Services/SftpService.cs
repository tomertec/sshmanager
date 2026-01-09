using System.IO;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for establishing SFTP connections using SSH.NET.
/// </summary>
public sealed class SftpService : ISftpService
{
    private readonly ILogger<SftpService> _logger;

    public SftpService(ILogger<SftpService>? logger = null)
    {
        _logger = logger ?? NullLogger<SftpService>.Instance;
    }

    public async Task<ISftpSession> ConnectAsync(TerminalConnectionInfo connectionInfo, CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting SFTP to {Host}:{Port} as {Username} using {AuthType}",
            connectionInfo.Hostname, connectionInfo.Port, connectionInfo.Username, connectionInfo.AuthType);

        var authMethods = CreateAuthMethods(connectionInfo);
        var connInfo = new ConnectionInfo(
            connectionInfo.Hostname,
            connectionInfo.Port,
            connectionInfo.Username,
            authMethods)
        {
            Timeout = connectionInfo.Timeout
        };

        var client = new SftpClient(connInfo);

        if (connectionInfo.KeepAliveInterval.HasValue &&
            connectionInfo.KeepAliveInterval.Value > TimeSpan.Zero)
        {
            client.KeepAliveInterval = connectionInfo.KeepAliveInterval.Value;
        }

        try
        {
            // Connect on background thread to avoid blocking UI
            await Task.Run(() => client.Connect(), ct);

            _logger.LogInformation("SFTP connection established to {Host}:{Port}",
                connectionInfo.Hostname, connectionInfo.Port);

            return new SftpSession(client, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SFTP connection to {Host}:{Port} failed: {Message}",
                connectionInfo.Hostname, connectionInfo.Port, ex.Message);
            client.Dispose();
            throw;
        }
    }

    private AuthenticationMethod[] CreateAuthMethods(TerminalConnectionInfo connectionInfo)
    {
        _logger.LogDebug("Creating authentication methods for {AuthType}", connectionInfo.AuthType);

        return connectionInfo.AuthType switch
        {
            AuthType.Password when !string.IsNullOrEmpty(connectionInfo.Password) =>
                CreatePasswordAuth(connectionInfo),

            AuthType.PrivateKeyFile when !string.IsNullOrEmpty(connectionInfo.PrivateKeyPath) =>
                CreatePrivateKeyAuth(connectionInfo),

            AuthType.SshAgent => CreateAgentAuth(connectionInfo),

            _ when connectionInfo.AuthType == AuthType.Password =>
                LogAndFallback(connectionInfo, "Password auth configured but no password provided"),

            _ => CreateAgentAuth(connectionInfo)
        };
    }

    private AuthenticationMethod[] LogAndFallback(TerminalConnectionInfo connectionInfo, string reason)
    {
        _logger.LogWarning("Falling back to agent auth: {Reason}", reason);
        return CreateAgentAuth(connectionInfo);
    }

    private AuthenticationMethod[] CreatePasswordAuth(TerminalConnectionInfo connectionInfo)
    {
        return [new PasswordAuthenticationMethod(connectionInfo.Username, connectionInfo.Password!)];
    }

    private AuthenticationMethod[] CreateAgentAuth(TerminalConnectionInfo connectionInfo)
    {
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var keyFiles = new List<PrivateKeyFile>();

        _logger.LogDebug("Searching for SSH keys in {SshDir}", sshDir);

        var defaultKeys = new[] { "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa" };
        foreach (var keyName in defaultKeys)
        {
            var keyPath = Path.Combine(sshDir, keyName);
            if (File.Exists(keyPath))
            {
                try
                {
                    keyFiles.Add(new PrivateKeyFile(keyPath));
                    _logger.LogDebug("Loaded SSH key: {KeyPath}", keyPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load SSH key {KeyPath}", keyPath);
                }
            }
        }

        if (keyFiles.Count > 0)
        {
            _logger.LogInformation("Using {KeyCount} SSH keys for SFTP authentication", keyFiles.Count);
            return [new PrivateKeyAuthenticationMethod(connectionInfo.Username, keyFiles.ToArray())];
        }

        _logger.LogWarning("No SSH keys found for SFTP authentication");
        return [new KeyboardInteractiveAuthenticationMethod(connectionInfo.Username)];
    }

    private AuthenticationMethod[] CreatePrivateKeyAuth(TerminalConnectionInfo connectionInfo)
    {
        _logger.LogDebug("Loading private key from {KeyPath}", connectionInfo.PrivateKeyPath);

        PrivateKeyFile keyFile;

        if (!string.IsNullOrEmpty(connectionInfo.PrivateKeyPassphrase))
        {
            keyFile = new PrivateKeyFile(connectionInfo.PrivateKeyPath!, connectionInfo.PrivateKeyPassphrase);
            _logger.LogDebug("Private key loaded with passphrase");
        }
        else
        {
            keyFile = new PrivateKeyFile(connectionInfo.PrivateKeyPath!);
            _logger.LogDebug("Private key loaded without passphrase");
        }

        return [new PrivateKeyAuthenticationMethod(connectionInfo.Username, keyFile)];
    }
}

/// <summary>
/// Wraps an SSH.NET SftpClient as an ISftpSession.
/// </summary>
internal sealed class SftpSession : ISftpSession
{
    private readonly SftpClient _client;
    private readonly ILogger _logger;
    private bool _disposed;

    public bool IsConnected => _client.IsConnected && !_disposed;
    public string WorkingDirectory => _client.WorkingDirectory;
    public event EventHandler? Disconnected;

    public SftpSession(SftpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;

        _client.ErrorOccurred += OnError;
    }

    /// <summary>
    /// Validates a local file path to prevent path traversal attacks.
    /// </summary>
    /// <param name="localPath">The local file path to validate.</param>
    /// <param name="expectedBasePath">Optional base path that the file must be within.</param>
    /// <exception cref="SecurityException">Thrown when path traversal is detected.</exception>
    /// <exception cref="ArgumentException">Thrown when the path is invalid.</exception>
    private static void ValidateLocalPath(string localPath, string? expectedBasePath = null)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new ArgumentException("Local path cannot be null or empty", nameof(localPath));
        }

        // Normalize the path to detect traversal attempts
        var fullPath = Path.GetFullPath(localPath);

        // Check for path traversal attempts using ".." sequences
        if (localPath.Contains(".."))
        {
            throw new SecurityException($"Path traversal detected in local path: {localPath}");
        }

        // Check for null bytes which could be used for path injection
        if (localPath.Contains('\0'))
        {
            throw new SecurityException("Null byte detected in local path");
        }

        // If base path specified, ensure we're within it
        if (!string.IsNullOrEmpty(expectedBasePath))
        {
            var baseFull = Path.GetFullPath(expectedBasePath);
            if (!fullPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Path '{localPath}' is outside allowed directory '{expectedBasePath}'");
            }
        }
    }

    /// <summary>
    /// Validates a remote file path for basic security checks.
    /// </summary>
    /// <param name="remotePath">The remote file path to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the path is invalid.</exception>
    private static void ValidateRemotePath(string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            throw new ArgumentException("Remote path cannot be null or empty", nameof(remotePath));
        }

        // Check for null bytes which could be used for path injection
        if (remotePath.Contains('\0'))
        {
            throw new SecurityException("Null byte detected in remote path");
        }
    }

    public async Task<IReadOnlyList<SftpFileItem>> ListDirectoryAsync(string path, CancellationToken ct = default)
    {
        _logger.LogDebug("Listing directory: {Path}", path);

        var files = await Task.Run(() => _client.ListDirectory(path), ct);

        var items = new List<SftpFileItem>();
        foreach (var file in files)
        {
            // Skip . and .. entries
            if (file.Name == "." || file.Name == "..")
                continue;

            items.Add(MapToSftpFileItem(file));
        }

        _logger.LogDebug("Found {Count} items in {Path}", items.Count, path);
        return items;
    }

    public async Task DownloadFileAsync(
        string remotePath,
        string localPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        long resumeOffset = 0)
    {
        // Validate paths before proceeding
        ValidateRemotePath(remotePath);
        ValidateLocalPath(localPath);

        _logger.LogInformation("Downloading {RemotePath} to {LocalPath} (resume: {ResumeOffset})",
            remotePath, localPath, resumeOffset);

        var fileInfo = _client.Get(remotePath);
        var totalBytes = fileInfo.Length;
        var startOffset = Math.Clamp(resumeOffset, 0, totalBytes);
        long downloadedBytes = 0;

        // Ensure parent directory exists
        var localDir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
        {
            Directory.CreateDirectory(localDir);
        }

        var fileMode = startOffset > 0 ? FileMode.OpenOrCreate : FileMode.Create;
        await using var localStream = new FileStream(localPath, fileMode, FileAccess.Write, FileShare.None);
        if (startOffset > 0)
        {
            localStream.Seek(startOffset, SeekOrigin.Begin);
        }

        await Task.Run(() =>
        {
            using var remoteStream = _client.OpenRead(remotePath);
            if (startOffset > 0)
            {
                remoteStream.Seek(startOffset, SeekOrigin.Begin);
            }

            var buffer = new byte[8192];
            int read;
            while ((read = remoteStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                localStream.Write(buffer, 0, read);
                downloadedBytes += read;

                if (totalBytes > 0)
                {
                    var percent = (double)(startOffset + downloadedBytes) / totalBytes * 100.0;
                    progress?.Report(percent);
                }
            }
        }, ct);

        _logger.LogInformation("Download complete: {RemotePath} ({Bytes} bytes)", remotePath, startOffset + downloadedBytes);
    }

    public async Task UploadFileAsync(
        string localPath,
        string remotePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        long resumeOffset = 0)
    {
        // Validate paths before proceeding
        ValidateLocalPath(localPath);
        ValidateRemotePath(remotePath);

        _logger.LogInformation("Uploading {LocalPath} to {RemotePath} (resume: {ResumeOffset})",
            localPath, remotePath, resumeOffset);

        var fileInfo = new FileInfo(localPath);
        var totalBytes = fileInfo.Length;
        var startOffset = Math.Clamp(resumeOffset, 0, totalBytes);
        long uploadedBytes = 0;

        await using var localStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (startOffset > 0)
        {
            localStream.Seek(startOffset, SeekOrigin.Begin);
        }

        await Task.Run(() =>
        {
            using var remoteStream = _client.Open(
                remotePath,
                startOffset > 0 ? FileMode.OpenOrCreate : FileMode.Create,
                FileAccess.Write);

            if (startOffset > 0)
            {
                remoteStream.Seek(startOffset, SeekOrigin.Begin);
            }

            var buffer = new byte[8192];
            int read;
            while ((read = localStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                remoteStream.Write(buffer, 0, read);
                uploadedBytes += read;

                if (totalBytes > 0)
                {
                    var percent = (double)(startOffset + uploadedBytes) / totalBytes * 100.0;
                    progress?.Report(percent);
                }
            }
        }, ct);

        _logger.LogInformation("Upload complete: {LocalPath} ({Bytes} bytes)", localPath, startOffset + uploadedBytes);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating directory: {Path}", path);
        await Task.Run(() => _client.CreateDirectory(path), ct);
    }

    public async Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting {Path} (recursive: {Recursive})", path, recursive);

        if (recursive)
        {
            await DeleteRecursiveAsync(path, ct);
        }
        else
        {
            var info = await GetFileInfoAsync(path, ct);
            if (info?.IsDirectory == true)
            {
                await Task.Run(() => _client.DeleteDirectory(path), ct);
            }
            else
            {
                await Task.Run(() => _client.DeleteFile(path), ct);
            }
        }
    }

    private async Task DeleteRecursiveAsync(string path, CancellationToken ct)
    {
        var info = await GetFileInfoAsync(path, ct);
        if (info == null)
            return;

        if (info.IsDirectory)
        {
            var items = await ListDirectoryAsync(path, ct);
            foreach (var item in items)
            {
                await DeleteRecursiveAsync(item.FullPath, ct);
            }
            await Task.Run(() => _client.DeleteDirectory(path), ct);
        }
        else
        {
            await Task.Run(() => _client.DeleteFile(path), ct);
        }
    }

    public async Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Renaming {OldPath} to {NewPath}", oldPath, newPath);
        await Task.Run(() => _client.RenameFile(oldPath, newPath), ct);
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        return await Task.Run(() => _client.Exists(path), ct);
    }

    public async Task<SftpFileItem?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var file = await Task.Run(() => _client.Get(path), ct);
            return MapToSftpFileItem(file);
        }
        catch (Renci.SshNet.Common.SftpPathNotFoundException)
        {
            return null;
        }
    }

    public async Task ChangePermissionsAsync(string path, int permissions, CancellationToken ct = default)
    {
        _logger.LogInformation("Changing permissions for {Path} to {Permissions}", path, permissions);
        var clamped = Math.Clamp(permissions, 0, short.MaxValue);
        await Task.Run(() => _client.ChangePermissions(path, (short)clamped), ct);
    }

    private static SftpFileItem MapToSftpFileItem(ISftpFile file)
    {
        // Extract permission bits from the mode (lower 9 bits: rwxrwxrwx)
        var permissionBits = 0;
        if (file.Attributes.OwnerCanRead) permissionBits |= 0x100;
        if (file.Attributes.OwnerCanWrite) permissionBits |= 0x080;
        if (file.Attributes.OwnerCanExecute) permissionBits |= 0x040;
        if (file.Attributes.GroupCanRead) permissionBits |= 0x020;
        if (file.Attributes.GroupCanWrite) permissionBits |= 0x010;
        if (file.Attributes.GroupCanExecute) permissionBits |= 0x008;
        if (file.Attributes.OthersCanRead) permissionBits |= 0x004;
        if (file.Attributes.OthersCanWrite) permissionBits |= 0x002;
        if (file.Attributes.OthersCanExecute) permissionBits |= 0x001;

        return new SftpFileItem
        {
            Name = file.Name,
            FullPath = file.FullName,
            Size = file.Length,
            ModifiedDate = file.LastWriteTime,
            IsDirectory = file.IsDirectory,
            Permissions = permissionBits,
            Owner = file.Attributes.UserId.ToString(),
            Group = file.Attributes.GroupId.ToString(),
            IsSymbolicLink = file.IsSymbolicLink
        };
    }

    public async Task<byte[]> ReadAllBytesAsync(string remotePath, CancellationToken ct = default)
    {
        ValidateRemotePath(remotePath);
        _logger.LogDebug("Reading all bytes from: {Path}", remotePath);

        return await Task.Run(() =>
        {
            using var stream = _client.OpenRead(remotePath);
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();
            _logger.LogDebug("Read {ByteCount} bytes from {Path}", bytes.Length, remotePath);
            return bytes;
        }, ct);
    }

    public async Task WriteAllBytesAsync(string remotePath, byte[] content, CancellationToken ct = default)
    {
        ValidateRemotePath(remotePath);
        _logger.LogDebug("Writing {ByteCount} bytes to: {Path}", content.Length, remotePath);

        await Task.Run(() =>
        {
            using var stream = _client.Open(remotePath, FileMode.Create, FileAccess.Write);
            stream.Write(content, 0, content.Length);
            _logger.LogInformation("Wrote {ByteCount} bytes to {Path}", content.Length, remotePath);
        }, ct);
    }

    private void OnError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
    {
        _logger.LogWarning(e.Exception, "SFTP error occurred");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing SFTP session");

        _client.ErrorOccurred -= OnError;

        try
        {
            await Task.Run(() =>
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                    _logger.LogDebug("SFTP client disconnected");
                }
                _client.Dispose();
                _logger.LogDebug("SFTP client disposed");
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing SFTP session");
        }

        _logger.LogInformation("SFTP session disposed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }
}
