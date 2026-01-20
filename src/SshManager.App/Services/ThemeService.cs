using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using Wpf.Ui.Appearance;

namespace SshManager.App.Services;

/// <summary>
/// Service for managing application theme with system theme following support.
/// </summary>
public sealed class AppThemeService : IAppThemeService, IDisposable
{
    private readonly ILogger<AppThemeService> _logger;
    private readonly ISettingsRepository _settingsRepository;
    private AppTheme _currentTheme = AppTheme.Dark;
    private bool _isMonitoring;
    private bool _disposed;
    
    public event EventHandler<AppTheme>? ThemeChanged;
    
    public AppTheme CurrentTheme => _currentTheme;
    
    public AppTheme EffectiveTheme => _currentTheme == AppTheme.System 
        ? GetSystemTheme() 
        : _currentTheme;
    
    public AppThemeService(
        ILogger<AppThemeService> logger,
        ISettingsRepository settingsRepository)
    {
        _logger = logger;
        _settingsRepository = settingsRepository;
    }
    
    public void Initialize()
    {
        if (_isMonitoring) return;
        
        // Listen for system theme changes
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        _isMonitoring = true;
        
        _logger.LogDebug("Theme service initialized with system theme monitoring");
    }
    
    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        
        _currentTheme = theme;
        ApplyTheme();
        
        // Persist theme setting
        _ = SaveThemeSettingAsync(theme);
        
        ThemeChanged?.Invoke(this, theme);
        _logger.LogInformation("Application theme changed to {Theme}", theme);
    }
    
    private void ApplyTheme()
    {
        var applicationTheme = _currentTheme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,
            AppTheme.System => GetSystemTheme() == AppTheme.Light 
                ? ApplicationTheme.Light 
                : ApplicationTheme.Dark,
            _ => ApplicationTheme.Dark
        };
        
        ApplicationThemeManager.Apply(applicationTheme);
    }
    
    private static AppTheme GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 1 ? AppTheme.Light : AppTheme.Dark;
            }
        }
        catch
        {
            // Fall back to dark theme if unable to read registry
        }
        
        return AppTheme.Dark;
    }
    
    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_currentTheme != AppTheme.System) return;
        
        // Re-apply theme when system theme changes
        ApplyTheme();
        ThemeChanged?.Invoke(this, _currentTheme);
        _logger.LogDebug("System theme changed, re-applied theme");
    }
    
    private async Task SaveThemeSettingAsync(AppTheme theme)
    {
        try
        {
            var settings = await _settingsRepository.GetAsync();
            settings.Theme = theme.ToString();
            await _settingsRepository.UpdateAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save theme setting");
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_isMonitoring)
        {
            SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
        }
    }
}
