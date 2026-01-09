using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for managing system tray integration.
/// </summary>
public interface ISystemTrayService : IDisposable
{
    /// <summary>
    /// Initializes the system tray icon.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Updates the context menu with current hosts and groups.
    /// </summary>
    void UpdateContextMenu(IEnumerable<HostEntry> hosts, IEnumerable<HostGroup> groups);

    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    void Show();

    /// <summary>
    /// Hides the tray icon.
    /// </summary>
    void Hide();

    /// <summary>
    /// Event raised when a host is selected for quick connect.
    /// </summary>
    event EventHandler<HostEntry>? QuickConnectRequested;

    /// <summary>
    /// Event raised when the user requests to show the main window.
    /// </summary>
    event EventHandler? ShowWindowRequested;

    /// <summary>
    /// Event raised when the user requests to exit the application.
    /// </summary>
    event EventHandler? ExitRequested;

    /// <summary>
    /// Event raised when the user requests to open settings.
    /// </summary>
    event EventHandler? SettingsRequested;
}
