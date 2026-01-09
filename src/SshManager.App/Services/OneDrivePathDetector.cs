using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace SshManager.App.Services;

/// <summary>
/// Detects OneDrive installation and provides sync folder paths.
/// </summary>
public class OneDrivePathDetector : IOneDrivePathDetector
{
    private readonly ILogger<OneDrivePathDetector> _logger;

    public OneDrivePathDetector(ILogger<OneDrivePathDetector>? logger = null)
    {
        _logger = logger ?? NullLogger<OneDrivePathDetector>.Instance;
    }

    /// <inheritdoc />
    public string? GetOneDrivePath()
    {
        // Method 1: Environment variables (preferred - most reliable)
        var envVars = new[]
        {
            "OneDriveCommercial",  // OneDrive for Business
            "OneDriveConsumer",     // Personal OneDrive
            "OneDrive"              // Generic fallback
        };

        foreach (var envVar in envVars)
        {
            var path = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _logger.LogDebug("Found OneDrive path via {EnvVar}: {Path}", envVar, path);
                return path;
            }
        }

        // Method 2: Registry fallback
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive");
            if (key != null)
            {
                var userFolder = key.GetValue("UserFolder") as string;
                if (!string.IsNullOrEmpty(userFolder) && Directory.Exists(userFolder))
                {
                    _logger.LogDebug("Found OneDrive path via registry: {Path}", userFolder);
                    return userFolder;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read OneDrive path from registry");
        }

        // Method 3: Check common paths
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive - Personal"),
        };

        foreach (var path in commonPaths)
        {
            if (Directory.Exists(path))
            {
                _logger.LogDebug("Found OneDrive path at common location: {Path}", path);
                return path;
            }
        }

        _logger.LogDebug("OneDrive path not found");
        return null;
    }

    /// <inheritdoc />
    public bool IsOneDriveAvailable()
    {
        return GetOneDrivePath() != null;
    }

    /// <inheritdoc />
    public string? GetDefaultSyncFolderPath()
    {
        var oneDrivePath = GetOneDrivePath();
        if (oneDrivePath == null)
        {
            return null;
        }

        return Path.Combine(oneDrivePath, "SshManager");
    }
}
