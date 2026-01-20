using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for managing application theme.
/// </summary>
public interface IAppThemeService
{
    /// <summary>
    /// Gets the currently applied theme.
    /// </summary>
    AppTheme CurrentTheme { get; }
    
    /// <summary>
    /// Sets and applies the specified theme.
    /// </summary>
    /// <param name="theme">The theme to apply.</param>
    void SetTheme(AppTheme theme);
    
    /// <summary>
    /// Event raised when the theme changes.
    /// </summary>
    event EventHandler<AppTheme>? ThemeChanged;
    
    /// <summary>
    /// Initializes theme monitoring (for System theme following).
    /// </summary>
    void Initialize();
    
    /// <summary>
    /// Gets the effective theme (resolved System theme to actual Light/Dark).
    /// </summary>
    AppTheme EffectiveTheme { get; }
}
