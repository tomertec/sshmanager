using System.Windows;
using SshManager.Data.Repositories;

namespace SshManager.App.Services;

/// <summary>
/// Manages window position, size, and state persistence using settings repository.
/// </summary>
public class WindowStateManager : IWindowStateManager
{
    private readonly ISettingsRepository _settingsRepo;
    private bool _minimizeToTray;

    public bool MinimizeToTray => _minimizeToTray;

    public WindowStateManager(ISettingsRepository settingsRepo)
    {
        _settingsRepo = settingsRepo;
    }

    public async Task RefreshSettingsAsync()
    {
        var settings = await _settingsRepo.GetAsync();
        _minimizeToTray = settings.MinimizeToTray;
    }

    public async Task LoadWindowStateAsync(Window window)
    {
        var settings = await _settingsRepo.GetAsync();
        _minimizeToTray = settings.MinimizeToTray;

        // Restore window position if enabled and values are saved
        if (settings.RememberWindowPosition &&
            settings.WindowX.HasValue &&
            settings.WindowY.HasValue &&
            settings.WindowWidth.HasValue &&
            settings.WindowHeight.HasValue)
        {
            // Validate the position is within screen bounds
            var screenWidth = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;
            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;

            var x = settings.WindowX.Value;
            var y = settings.WindowY.Value;
            var width = settings.WindowWidth.Value;
            var height = settings.WindowHeight.Value;

            // Ensure window is at least partially visible
            if (x + width > screenLeft && x < screenLeft + screenWidth &&
                y + height > screenTop && y < screenTop + screenHeight)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = x;
                window.Top = y;
                window.Width = width;
                window.Height = height;
            }
        }
    }

    public async Task SaveWindowStateAsync(Window window)
    {
        try
        {
            var settings = await _settingsRepo.GetAsync();
            if (settings.RememberWindowPosition && window.WindowState == WindowState.Normal)
            {
                settings.WindowX = (int)window.Left;
                settings.WindowY = (int)window.Top;
                settings.WindowWidth = (int)window.Width;
                settings.WindowHeight = (int)window.Height;
                await _settingsRepo.UpdateAsync(settings);
            }
        }
        catch
        {
            // Ignore errors during shutdown - window position is not critical
        }
    }
}
