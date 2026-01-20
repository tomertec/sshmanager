using System.Windows;

namespace SshManager.App.Services;

/// <summary>
/// Manages window position, size, and state persistence.
/// </summary>
public interface IWindowStateManager
{
    /// <summary>
    /// Loads and applies saved window state (position, size) from settings.
    /// </summary>
    /// <param name="window">The window to apply state to.</param>
    /// <returns>A task representing the async operation.</returns>
    Task LoadWindowStateAsync(Window window);

    /// <summary>
    /// Saves the current window state (position, size) to settings.
    /// Only saves when the window is in normal state (not minimized/maximized).
    /// </summary>
    /// <param name="window">The window to save state from.</param>
    /// <returns>A task representing the async operation.</returns>
    Task SaveWindowStateAsync(Window window);

    /// <summary>
    /// Gets whether the minimize to tray setting is enabled.
    /// </summary>
    bool MinimizeToTray { get; }

    /// <summary>
    /// Refreshes the minimize to tray setting from storage.
    /// </summary>
    Task RefreshSettingsAsync();
}
