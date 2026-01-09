namespace SshManager.Core.Models;

/// <summary>
/// Represents a file or directory on a remote SFTP server.
/// </summary>
public sealed class SftpFileItem
{
    /// <summary>
    /// The file or directory name (without path).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The full path on the remote server.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// File size in bytes (0 for directories).
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    public DateTimeOffset ModifiedDate { get; init; }

    /// <summary>
    /// True if this item is a directory.
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Unix file permissions (e.g., 0755).
    /// </summary>
    public int Permissions { get; init; }

    /// <summary>
    /// Owner username on the remote system.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Group name on the remote system.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// True if this is a symbolic link.
    /// </summary>
    public bool IsSymbolicLink { get; init; }

    /// <summary>
    /// Target path if this is a symbolic link.
    /// </summary>
    public string? LinkTarget { get; init; }
}
