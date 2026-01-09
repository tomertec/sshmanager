using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for launching SSH connections in an external terminal application (Windows Terminal).
/// </summary>
public interface IExternalTerminalService
{
    /// <summary>
    /// Launches an SSH connection in Windows Terminal.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="password">Optional password for password authentication (note: limited support).</param>
    /// <returns>True if the terminal was launched successfully, false otherwise.</returns>
    Task<bool> LaunchSshConnectionAsync(HostEntry host, string? password = null);

    /// <summary>
    /// Checks if Windows Terminal is available on the system.
    /// </summary>
    bool IsWindowsTerminalAvailable { get; }
}
