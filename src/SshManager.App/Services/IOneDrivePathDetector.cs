namespace SshManager.App.Services;

/// <summary>
/// Service for detecting OneDrive installation and sync folder paths.
/// </summary>
public interface IOneDrivePathDetector
{
    /// <summary>
    /// Gets the OneDrive sync folder path.
    /// </summary>
    /// <returns>The OneDrive path, or null if OneDrive is not available.</returns>
    string? GetOneDrivePath();

    /// <summary>
    /// Checks if OneDrive is available on this system.
    /// </summary>
    bool IsOneDriveAvailable();

    /// <summary>
    /// Gets the default sync folder path for SshManager within OneDrive.
    /// </summary>
    /// <returns>The path to OneDrive\SshManager, or null if OneDrive is not available.</returns>
    string? GetDefaultSyncFolderPath();
}
