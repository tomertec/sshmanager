using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Data.Repositories;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel responsible for session logging controls.
/// Manages logging state, log file access, and logging settings for the current session.
/// </summary>
public partial class SessionLoggingViewModel : ObservableObject, IDisposable
{
    private readonly SessionViewModel _sessionViewModel;
    private readonly ISessionLoggingService _sessionLoggingService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<SessionLoggingViewModel> _logger;

    public SessionLoggingViewModel(
        SessionViewModel sessionViewModel,
        ISessionLoggingService sessionLoggingService,
        ISettingsRepository settingsRepo,
        ILogger<SessionLoggingViewModel>? logger = null)
    {
        _sessionViewModel = sessionViewModel;
        _sessionLoggingService = sessionLoggingService;
        _settingsRepo = settingsRepo;
        _logger = logger ?? NullLogger<SessionLoggingViewModel>.Instance;

        // Subscribe to CurrentSession changes from SessionViewModel
        _sessionViewModel.PropertyChanged += OnSessionViewModelPropertyChanged;

        _logger.LogDebug("SessionLoggingViewModel initialized");
    }

    private void OnSessionViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.CurrentSession))
        {
            OnCurrentSessionChanged();
        }
    }

    /// <summary>
    /// Whether the current session is actively logging.
    /// </summary>
    public bool IsCurrentSessionLogging => _sessionViewModel.CurrentSession?.SessionLogger?.IsLogging == true;

    /// <summary>
    /// The log file path for the current session.
    /// </summary>
    public string? CurrentSessionLogPath => _sessionViewModel.CurrentSession?.SessionLogger?.LogFilePath;

    /// <summary>
    /// Available session log levels for UI binding.
    /// </summary>
    public IReadOnlyList<SessionLogLevel> AvailableSessionLogLevels { get; } =
        Enum.GetValues<SessionLogLevel>();

    /// <summary>
    /// Gets or sets the log level for the current session.
    /// </summary>
    public SessionLogLevel CurrentSessionLogLevel
    {
        get => _sessionViewModel.CurrentSession?.LogLevel ?? SessionLogLevel.OutputAndEvents;
        set
        {
            var currentSession = _sessionViewModel.CurrentSession;
            if (currentSession == null || currentSession.LogLevel == value) return;

            currentSession.LogLevel = value;
            if (currentSession.SessionLogger != null)
            {
                currentSession.SessionLogger.LogLevel = value;
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets whether typed secrets should be redacted in the current session's log.
    /// </summary>
    public bool CurrentSessionRedactTypedSecrets
    {
        get => _sessionViewModel.CurrentSession?.RedactTypedSecrets ?? false;
        set
        {
            var currentSession = _sessionViewModel.CurrentSession;
            if (currentSession == null || currentSession.RedactTypedSecrets == value) return;

            currentSession.RedactTypedSecrets = value;
            if (currentSession.SessionLogger != null)
            {
                currentSession.SessionLogger.RedactTypedSecrets = value;
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Sets the log level for the current session.
    /// </summary>
    [RelayCommand]
    private void SetCurrentSessionLogLevel(SessionLogLevel level)
    {
        CurrentSessionLogLevel = level;
    }

    /// <summary>
    /// Toggles session logging for the current session.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleSessionLogging))]
    private async Task ToggleSessionLoggingAsync()
    {
        var session = _sessionViewModel.CurrentSession;
        if (session?.Host == null) return;

        var settings = await _settingsRepo.GetAsync();

        if (session.SessionLogger?.IsLogging == true)
        {
            // Stop logging
            _logger.LogInformation("Stopping session logging for session {SessionId}", session.Id);
            session.SessionLogger.LogEvent("SESSION", "Logging stopped by user");
            _sessionLoggingService.StopLogging(session.Id);
            session.SessionLogger = null;
        }
        else
        {
            // Start logging
            _logger.LogInformation("Starting session logging for session {SessionId}", session.Id);

            // Apply settings
            if (!string.IsNullOrEmpty(settings.SessionLogDirectory))
            {
                _sessionLoggingService.SetLogDirectory(settings.SessionLogDirectory);
            }
            _sessionLoggingService.SetTimestampEachLine(settings.SessionLogTimestampLines);
            _sessionLoggingService.SetMaxLogFileSizeMB(settings.MaxLogFileSizeMB);
            _sessionLoggingService.SetMaxLogFilesToKeep(settings.MaxLogFilesToKeep);

            var sessionTitle = $"{session.Host.DisplayName}_{session.Host.Hostname}";
            var logLevel = ParseSessionLogLevel(settings.SessionLogLevel);
            session.LogLevel = logLevel;
            session.RedactTypedSecrets = settings.RedactTypedSecrets;
            session.SessionLogger = _sessionLoggingService.StartLogging(
                session.Id,
                sessionTitle,
                logLevel,
                session.RedactTypedSecrets);
            session.SessionLogger.LogEvent("SESSION", "Logging started by user");
        }

        OnPropertyChanged(nameof(IsCurrentSessionLogging));
        OnPropertyChanged(nameof(CurrentSessionLogPath));
    }

    private bool CanToggleSessionLogging() => _sessionViewModel.CurrentSession != null;

    /// <summary>
    /// Opens the current session's log file in the default application.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenCurrentSessionLogFile))]
    private void OpenCurrentSessionLogFile()
    {
        var logPath = CurrentSessionLogPath;
        if (string.IsNullOrEmpty(logPath))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
            _logger.LogInformation("Opened log file: {LogPath}", logPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log file: {LogPath}", logPath);
        }
    }

    private bool CanOpenCurrentSessionLogFile()
    {
        var logPath = CurrentSessionLogPath;
        return !string.IsNullOrEmpty(logPath) && File.Exists(logPath);
    }

    /// <summary>
    /// Opens the session log directory in Windows Explorer.
    /// </summary>
    [RelayCommand]
    private void OpenLogDirectory()
    {
        var logDir = _sessionLoggingService.GetLogDirectory();

        // Ensure directory exists
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
            _logger.LogInformation("Opened log directory: {LogDir}", logDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log directory: {LogDir}", logDir);
        }
    }

    /// <summary>
    /// Called when the current session changes to update command states and properties.
    /// </summary>
    private void OnCurrentSessionChanged()
    {
        ToggleSessionLoggingCommand.NotifyCanExecuteChanged();
        OpenCurrentSessionLogFileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCurrentSessionLogging));
        OnPropertyChanged(nameof(CurrentSessionLogPath));
        OnPropertyChanged(nameof(CurrentSessionLogLevel));
        OnPropertyChanged(nameof(CurrentSessionRedactTypedSecrets));
    }

    private static SessionLogLevel ParseSessionLogLevel(string? value)
    {
        return Enum.TryParse(value, true, out SessionLogLevel parsed)
            ? parsed
            : SessionLogLevel.OutputAndEvents;
    }

    public void Dispose()
    {
        _sessionViewModel.PropertyChanged -= OnSessionViewModelPropertyChanged;
    }
}
