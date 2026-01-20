namespace SshManager.Terminal.Services;

/// <summary>
/// Represents the status of an X11 server on the local machine.
/// </summary>
/// <param name="IsAvailable">Whether an X server is detected and accessible.</param>
/// <param name="DisplayAddress">The display address (e.g., "localhost:0").</param>
/// <param name="DisplayNumber">The display number (0, 1, etc.).</param>
/// <param name="ServerName">Name of the detected X server (VcXsrv, Xming, X410, etc.).</param>
public record X11ServerStatus(
    bool IsAvailable,
    string DisplayAddress,
    int DisplayNumber,
    string? ServerName);

/// <summary>
/// Settings for X11 forwarding on a connection.
/// </summary>
/// <param name="Enabled">Whether X11 forwarding is enabled.</param>
/// <param name="Trusted">Whether to use trusted forwarding (-Y vs -X).</param>
/// <param name="DisplayNumber">Display number to forward to.</param>
public record X11ForwardingSettings(
    bool Enabled,
    bool Trusted,
    int DisplayNumber);

/// <summary>
/// Service for managing X11 forwarding on SSH connections.
/// Supports detection and launching of Windows X servers (VcXsrv, Xming, X410, etc.).
/// </summary>
public interface IX11ForwardingService
{
    /// <summary>
    /// Detects running X servers on the local machine.
    /// Checks TCP ports 6000+display, named pipes, and known processes.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Status information about the detected X server.</returns>
    Task<X11ServerStatus> DetectXServerAsync(CancellationToken ct = default);

    /// <summary>
    /// Launches the configured X server application.
    /// </summary>
    /// <param name="path">Path to the X server executable.</param>
    /// <param name="displayNumber">Display number to use.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the server was launched successfully.</returns>
    Task<bool> LaunchXServerAsync(string path, int displayNumber = 0, CancellationToken ct = default);

    /// <summary>
    /// Gets the DISPLAY environment variable value for the given display number.
    /// </summary>
    /// <param name="displayNumber">The X11 display number.</param>
    /// <returns>The DISPLAY value (e.g., "localhost:0").</returns>
    string GetDisplayValue(int displayNumber);
}
