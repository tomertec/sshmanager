using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly IConnectionHistoryRepository _historyRepo;
    private readonly ICredentialCache _credentialCache;
    private readonly ITerminalThemeService _themeService;
    private AppSettings? _settings;

    // ===== Terminal Settings =====
    [ObservableProperty]
    private bool _useEmbeddedTerminal = true;

    [ObservableProperty]
    private string _terminalFontFamily = "Cascadia Mono";

    [ObservableProperty]
    private int _terminalFontSize = 14;

    [ObservableProperty]
    private string _terminalThemeId = "default";

    [ObservableProperty]
    private TerminalTheme? _selectedTerminalTheme;

    // ===== Connection Settings =====
    [ObservableProperty]
    private int _defaultPort = 22;

    [ObservableProperty]
    private int _connectionTimeoutSeconds = 30;

    [ObservableProperty]
    private int _keepAliveIntervalSeconds = 60;

    [ObservableProperty]
    private bool _autoReconnect = false;

    [ObservableProperty]
    private int _maxReconnectAttempts = 3;

    // ===== Security Settings =====
    [ObservableProperty]
    private string _defaultKeyPath = "";

    [ObservableProperty]
    private string _preferredAuthMethod = "SshAgent";

    // ===== Credential Caching Settings =====
    [ObservableProperty]
    private bool _enableCredentialCaching = false;

    [ObservableProperty]
    private int _credentialCacheTimeoutMinutes = 15;

    [ObservableProperty]
    private bool _clearCacheOnLock = true;

    [ObservableProperty]
    private bool _clearCacheOnExit = true;

    // ===== Application Behavior =====
    [ObservableProperty]
    private bool _confirmOnClose = true;

    [ObservableProperty]
    private bool _rememberWindowPosition = true;

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private bool _startMinimized = false;

    [ObservableProperty]
    private bool _minimizeToTray = false;

    // ===== Session Logging Settings =====
    [ObservableProperty]
    private bool _enableSessionLogging = false;

    [ObservableProperty]
    private string _sessionLogDirectory = "";

    [ObservableProperty]
    private bool _sessionLogTimestampLines = true;

    [ObservableProperty]
    private int _maxLogFileSizeMB = 50;

    [ObservableProperty]
    private int _maxLogFilesToKeep = 5;

    [ObservableProperty]
    private string _sessionLogLevel = "OutputAndEvents";

    [ObservableProperty]
    private bool _redactTypedSecrets;

    // ===== History Settings =====
    [ObservableProperty]
    private int _maxHistoryEntries = 100;

    [ObservableProperty]
    private int _historyRetentionDays = 0;

    // ===== Backup Settings =====
    [ObservableProperty]
    private bool _enableAutoBackup = false;

    [ObservableProperty]
    private int _backupIntervalMinutes = 60;

    [ObservableProperty]
    private int _maxBackupCount = 10;

    [ObservableProperty]
    private string _backupDirectory = "";

    // ===== UI State =====
    [ObservableProperty]
    private bool _isSaving;

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    // Available font families for terminal
    public IReadOnlyList<string> AvailableFonts { get; } = new[]
    {
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Courier New",
        "Lucida Console",
        "Source Code Pro",
        "Fira Code",
        "JetBrains Mono"
    };

    // Available themes
    public IReadOnlyList<string> AvailableThemes { get; } = new[]
    {
        "Dark",
        "Light",
        "System"
    };

    // Available authentication methods
    public IReadOnlyList<string> AvailableAuthMethods { get; } = new[]
    {
        "SshAgent",
        "PrivateKeyFile",
        "Password"
    };

    public IReadOnlyList<string> AvailableSessionLogLevels { get; } = new[]
    {
        "OutputAndEvents",
        "EventsOnly",
        "ErrorsOnly"
    };

    // Available terminal themes from theme service
    public IReadOnlyList<TerminalTheme> AvailableTerminalThemes => _themeService.GetAllThemes();

    // Event to notify when terminal theme changes (for live preview)
    public event EventHandler<TerminalTheme>? TerminalThemeChanged;

    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        IConnectionHistoryRepository historyRepo,
        ICredentialCache credentialCache,
        ITerminalThemeService themeService)
    {
        _settingsRepo = settingsRepo;
        _historyRepo = historyRepo;
        _credentialCache = credentialCache;
        _themeService = themeService;
    }

    public async Task LoadAsync()
    {
        _settings = await _settingsRepo.GetAsync();

        // Terminal settings
        UseEmbeddedTerminal = _settings.UseEmbeddedTerminal;
        TerminalFontFamily = _settings.TerminalFontFamily;
        TerminalFontSize = _settings.TerminalFontSize;
        TerminalThemeId = _settings.TerminalThemeId;
        SelectedTerminalTheme = _themeService.GetTheme(TerminalThemeId) ?? _themeService.GetTheme("default");

        // Connection settings
        DefaultPort = _settings.DefaultPort;
        ConnectionTimeoutSeconds = _settings.ConnectionTimeoutSeconds;
        KeepAliveIntervalSeconds = _settings.KeepAliveIntervalSeconds;
        AutoReconnect = _settings.AutoReconnect;
        MaxReconnectAttempts = _settings.MaxReconnectAttempts;

        // Security settings
        DefaultKeyPath = _settings.DefaultKeyPath;
        PreferredAuthMethod = _settings.PreferredAuthMethod;

        // Credential caching settings
        EnableCredentialCaching = _settings.EnableCredentialCaching;
        CredentialCacheTimeoutMinutes = _settings.CredentialCacheTimeoutMinutes;
        ClearCacheOnLock = _settings.ClearCacheOnLock;
        ClearCacheOnExit = _settings.ClearCacheOnExit;

        // Application behavior
        ConfirmOnClose = _settings.ConfirmOnClose;
        RememberWindowPosition = _settings.RememberWindowPosition;
        Theme = _settings.Theme;
        StartMinimized = _settings.StartMinimized;
        MinimizeToTray = _settings.MinimizeToTray;

        // Session logging settings
        EnableSessionLogging = _settings.EnableSessionLogging;
        SessionLogDirectory = _settings.SessionLogDirectory;
        SessionLogTimestampLines = _settings.SessionLogTimestampLines;
        MaxLogFileSizeMB = _settings.MaxLogFileSizeMB;
        MaxLogFilesToKeep = _settings.MaxLogFilesToKeep;
        SessionLogLevel = _settings.SessionLogLevel;
        RedactTypedSecrets = _settings.RedactTypedSecrets;

        // History settings
        MaxHistoryEntries = _settings.MaxHistoryEntries;
        HistoryRetentionDays = _settings.HistoryRetentionDays;

        // Backup settings
        EnableAutoBackup = _settings.EnableAutoBackup;
        BackupIntervalMinutes = _settings.BackupIntervalMinutes;
        MaxBackupCount = _settings.MaxBackupCount;
        BackupDirectory = _settings.BackupDirectory ?? "";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_settings == null) return;

        IsSaving = true;
        try
        {
            // Terminal settings
            _settings.UseEmbeddedTerminal = UseEmbeddedTerminal;
            _settings.TerminalFontFamily = TerminalFontFamily;
            _settings.TerminalFontSize = TerminalFontSize;
            _settings.TerminalThemeId = SelectedTerminalTheme?.Id ?? "default";

            // Connection settings
            _settings.DefaultPort = DefaultPort;
            _settings.ConnectionTimeoutSeconds = ConnectionTimeoutSeconds;
            _settings.KeepAliveIntervalSeconds = KeepAliveIntervalSeconds;
            _settings.AutoReconnect = AutoReconnect;
            _settings.MaxReconnectAttempts = MaxReconnectAttempts;

            // Security settings
            _settings.DefaultKeyPath = DefaultKeyPath;
            _settings.PreferredAuthMethod = PreferredAuthMethod;

            // Credential caching settings
            _settings.EnableCredentialCaching = EnableCredentialCaching;
            _settings.CredentialCacheTimeoutMinutes = CredentialCacheTimeoutMinutes;
            _settings.ClearCacheOnLock = ClearCacheOnLock;
            _settings.ClearCacheOnExit = ClearCacheOnExit;

            // Update credential cache timeout if changed
            if (EnableCredentialCaching && CredentialCacheTimeoutMinutes > 0)
            {
                _credentialCache.SetTimeout(TimeSpan.FromMinutes(CredentialCacheTimeoutMinutes));
            }

            // Application behavior
            _settings.ConfirmOnClose = ConfirmOnClose;
            _settings.RememberWindowPosition = RememberWindowPosition;
            _settings.Theme = Theme;
            _settings.StartMinimized = StartMinimized;
            _settings.MinimizeToTray = MinimizeToTray;

        // Session logging settings
        _settings.EnableSessionLogging = EnableSessionLogging;
        _settings.SessionLogDirectory = SessionLogDirectory;
        _settings.SessionLogTimestampLines = SessionLogTimestampLines;
        _settings.MaxLogFileSizeMB = MaxLogFileSizeMB;
        _settings.MaxLogFilesToKeep = MaxLogFilesToKeep;
        _settings.SessionLogLevel = SessionLogLevel;
        _settings.RedactTypedSecrets = RedactTypedSecrets;

            // History settings
            _settings.MaxHistoryEntries = MaxHistoryEntries;
            _settings.HistoryRetentionDays = HistoryRetentionDays;

            // Backup settings
            _settings.EnableAutoBackup = EnableAutoBackup;
            _settings.BackupIntervalMinutes = BackupIntervalMinutes;
            _settings.MaxBackupCount = MaxBackupCount;
            _settings.BackupDirectory = string.IsNullOrWhiteSpace(BackupDirectory) ? null : BackupDirectory;

            await _settingsRepo.UpdateAsync(_settings);

            DialogResult = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void SetEmbeddedTerminal(string value)
    {
        UseEmbeddedTerminal = value.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to clear all connection history? This cannot be undone.",
            "Clear History",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await _historyRepo.ClearAllAsync();
            System.Windows.MessageBox.Show(
                "Connection history has been cleared.",
                "History Cleared",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void BrowseLogDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Session Log Directory"
        };

        if (!string.IsNullOrEmpty(SessionLogDirectory) && System.IO.Directory.Exists(SessionLogDirectory))
        {
            dialog.InitialDirectory = SessionLogDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            SessionLogDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var path = string.IsNullOrEmpty(SessionLogDirectory)
            ? GetDefaultSessionLogDirectory()
            : SessionLogDirectory;

        if (System.IO.Directory.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        else
        {
            System.Windows.MessageBox.Show(
                "Log directory does not exist yet. It will be created when logging starts.",
                "Directory Not Found",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void ClearCredentialCache()
    {
        var count = _credentialCache.Count;
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                "No credentials are currently cached.",
                "Cache Empty",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to clear {count} cached credential(s)?\n\nYou will need to re-enter passwords on next connection.",
            "Clear Credential Cache",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _credentialCache.ClearAll();
            System.Windows.MessageBox.Show(
                "Credential cache has been cleared.",
                "Cache Cleared",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    private static string GetDefaultSessionLogDirectory()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SshManager",
            "sessions");
    }

    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Backup Directory"
        };

        if (!string.IsNullOrEmpty(BackupDirectory) && System.IO.Directory.Exists(BackupDirectory))
        {
            dialog.InitialDirectory = BackupDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            BackupDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void OpenBackupDirectory()
    {
        var path = string.IsNullOrEmpty(BackupDirectory)
            ? GetDefaultBackupDirectory()
            : BackupDirectory;

        if (System.IO.Directory.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        else
        {
            System.Windows.MessageBox.Show(
                "Backup directory does not exist yet. It will be created when a backup is made.",
                "Directory Not Found",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    private static string GetDefaultBackupDirectory()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SshManager",
            "backups");
    }

    partial void OnSelectedTerminalThemeChanged(TerminalTheme? value)
    {
        if (value != null)
        {
            TerminalThemeId = value.Id;
            TerminalThemeChanged?.Invoke(this, value);
        }
    }

    [RelayCommand]
    private async Task ImportTerminalThemeAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Terminal Theme",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() == true)
        {
            var theme = await _themeService.ImportThemeFromFileAsync(dialog.FileName);
            if (theme != null)
            {
                _themeService.AddCustomTheme(theme);
                await _themeService.SaveCustomThemesAsync();
                OnPropertyChanged(nameof(AvailableTerminalThemes));

                System.Windows.MessageBox.Show(
                    $"Theme '{theme.Name}' imported successfully!",
                    "Theme Imported",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "Failed to import theme. The file may be invalid or corrupted.",
                    "Import Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task ExportTerminalThemeAsync()
    {
        if (SelectedTerminalTheme == null)
        {
            System.Windows.MessageBox.Show(
                "Please select a theme to export.",
                "No Theme Selected",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Terminal Theme",
            Filter = "JSON Files (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"{SelectedTerminalTheme.Name.Replace(" ", "-").ToLower()}-theme.json"
        };

        if (dialog.ShowDialog() == true)
        {
            await _themeService.ExportThemeToFileAsync(SelectedTerminalTheme, dialog.FileName);

            System.Windows.MessageBox.Show(
                $"Theme '{SelectedTerminalTheme.Name}' exported successfully!",
                "Theme Exported",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task DeleteTerminalThemeAsync()
    {
        if (SelectedTerminalTheme == null || SelectedTerminalTheme.IsBuiltIn)
        {
            System.Windows.MessageBox.Show(
                "Built-in themes cannot be deleted.",
                "Cannot Delete Theme",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to delete the theme '{SelectedTerminalTheme.Name}'?",
            "Delete Theme",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            var themeToDelete = SelectedTerminalTheme;
            _themeService.RemoveCustomTheme(themeToDelete.Id);
            await _themeService.SaveCustomThemesAsync();
            SelectedTerminalTheme = _themeService.GetTheme("default");
            OnPropertyChanged(nameof(AvailableTerminalThemes));

            System.Windows.MessageBox.Show(
                $"Theme '{themeToDelete.Name}' deleted.",
                "Theme Deleted",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }
}
