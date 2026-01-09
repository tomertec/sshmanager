using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using IBroadcastInputService = SshManager.Terminal.Services.IBroadcastInputService;
using SshManager.App.Services;
using SshManager.App.Views.Dialogs;
using SshManager.App.Views.Windows;

namespace SshManager.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IHostRepository _hostRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IConnectionHistoryRepository _historyRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IHostFingerprintRepository _fingerprintRepo;
    private readonly IProxyJumpProfileRepository _proxyJumpRepo;
    private readonly IPortForwardingProfileRepository _portForwardingRepo;
    private readonly ISecretProtector _secretProtector;
    private readonly ICredentialCache _credentialCache;
    private readonly ISshConnectionService _sshService;
    private readonly ISftpService _sftpService;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ISessionLoggingService _sessionLoggingService;
    private readonly IExportImportService _exportImportService;
    private readonly IBroadcastInputService _broadcastService;
    private readonly IHostStatusService _hostStatusService;
    private readonly ITerminalResizeService _terminalResizeService;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<HostEntry> _hosts = [];

    [ObservableProperty]
    private ObservableCollection<HostGroup> _groups = [];

    [ObservableProperty]
    private HostEntry? _selectedHost;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    private CancellationTokenSource? _searchCancellationTokenSource;

    [ObservableProperty]
    private HostGroup? _selectedGroupFilter;

    public ObservableCollection<TerminalSession> Sessions => _sessionManager.Sessions;

    [ObservableProperty]
    private TerminalSession? _currentSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentSessionLogging))]
    [NotifyPropertyChangedFor(nameof(CurrentSessionLogPath))]
    private bool _isLoggingEnabled;

    public bool HasActiveSessions => Sessions.Count > 0;

    /// <summary>
    /// Gets host statuses for UI binding.
    /// </summary>
    public IReadOnlyDictionary<Guid, HostStatus> HostStatuses => _hostStatusService.GetAllStatuses();

    /// <summary>
    /// Whether the current session is actively logging.
    /// </summary>
    public bool IsCurrentSessionLogging => CurrentSession?.SessionLogger?.IsLogging == true;

    /// <summary>
    /// The log file path for the current session.
    /// </summary>
    public string? CurrentSessionLogPath => CurrentSession?.SessionLogger?.LogFilePath;

    public IReadOnlyList<SessionLogLevel> AvailableSessionLogLevels { get; } =
        Enum.GetValues<SessionLogLevel>();

    public SessionLogLevel CurrentSessionLogLevel
    {
        get => CurrentSession?.LogLevel ?? SessionLogLevel.OutputAndEvents;
        set
        {
            if (CurrentSession == null || CurrentSession.LogLevel == value) return;

            CurrentSession.LogLevel = value;
            if (CurrentSession.SessionLogger != null)
            {
                CurrentSession.SessionLogger.LogLevel = value;
            }
            OnPropertyChanged();
        }
    }

    public bool CurrentSessionRedactTypedSecrets
    {
        get => CurrentSession?.RedactTypedSecrets ?? false;
        set
        {
            if (CurrentSession == null || CurrentSession.RedactTypedSecrets == value) return;

            CurrentSession.RedactTypedSecrets = value;
            if (CurrentSession.SessionLogger != null)
            {
                CurrentSession.SessionLogger.RedactTypedSecrets = value;
            }
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void SetCurrentSessionLogLevel(SessionLogLevel level)
    {
        CurrentSessionLogLevel = level;
    }

    /// <summary>
    /// Whether broadcast input mode is enabled.
    /// </summary>
    public bool IsBroadcastMode
    {
        get => _sessionManager.IsBroadcastMode;
        set
        {
            if (_sessionManager.IsBroadcastMode != value)
            {
                _sessionManager.IsBroadcastMode = value;
                OnPropertyChanged(nameof(IsBroadcastMode));
                OnPropertyChanged(nameof(BroadcastSelectedCount));
            }
        }
    }

    /// <summary>
    /// Number of sessions selected for broadcast.
    /// </summary>
    public int BroadcastSelectedCount => _sessionManager.BroadcastSelectedCount;

    /// <summary>
    /// Port forwarding manager for managing active port forwards.
    /// </summary>
    public PortForwardingManagerViewModel PortForwardingManager { get; }

    /// <summary>
    /// Event raised when a new session is created (for pane management).
    /// </summary>
    public event EventHandler<TerminalSession>? SessionCreated;

    public MainWindowViewModel(
        IHostRepository hostRepo,
        IGroupRepository groupRepo,
        IConnectionHistoryRepository historyRepo,
        ISettingsRepository settingsRepo,
        IHostFingerprintRepository fingerprintRepo,
        IProxyJumpProfileRepository proxyJumpRepo,
        IPortForwardingProfileRepository portForwardingRepo,
        ISecretProtector secretProtector,
        ICredentialCache credentialCache,
        ISshConnectionService sshService,
        ISftpService sftpService,
        ITerminalSessionManager sessionManager,
        ISessionLoggingService sessionLoggingService,
        IExportImportService exportImportService,
        IBroadcastInputService broadcastService,
        IHostStatusService hostStatusService,
        ITerminalResizeService terminalResizeService,
        PortForwardingManagerViewModel portForwardingManager,
        ILogger<MainWindowViewModel>? logger = null)
    {
        _hostRepo = hostRepo;
        _groupRepo = groupRepo;
        _historyRepo = historyRepo;
        _settingsRepo = settingsRepo;
        _fingerprintRepo = fingerprintRepo;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _secretProtector = secretProtector;
        _credentialCache = credentialCache;
        _sshService = sshService;
        _sftpService = sftpService;
        _sessionManager = sessionManager;
        _sessionLoggingService = sessionLoggingService;
        _exportImportService = exportImportService;
        _broadcastService = broadcastService;
        _hostStatusService = hostStatusService;
        _terminalResizeService = terminalResizeService;
        PortForwardingManager = portForwardingManager;
        _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;

        _logger.LogDebug("MainWindowViewModel initializing");

        // Startup validation: warn if terminal resize is not available
        if (!_terminalResizeService.IsResizeSupported)
        {
            _logger.LogWarning(
                "Terminal resize is not available - terminal windows may not resize properly. " +
                "This may be due to an SSH.NET library version change.");
        }

        // Subscribe to host status changes
        _hostStatusService.StatusChanged += (s, e) =>
        {
            // Notify that HostStatuses has changed (UI will re-query)
            OnPropertyChanged(nameof(HostStatuses));
        };

        // Subscribe to session events
        _sessionManager.SessionCreated += (s, session) => OnPropertyChanged(nameof(HasActiveSessions));
        _sessionManager.SessionClosed += (s, session) => OnPropertyChanged(nameof(HasActiveSessions));

        // Sync current session
        _sessionManager.CurrentSessionChanged += (s, session) =>
        {
            CurrentSession = session;
        };

        _logger.LogDebug("MainWindowViewModel initialized");
    }

    public async Task LoadDataAsync()
    {
        _logger.LogDebug("Loading hosts and groups from database");
        IsLoading = true;
        try
        {
            var hosts = await _hostRepo.GetAllAsync();
            Hosts = new ObservableCollection<HostEntry>(hosts);
            _logger.LogInformation("Loaded {HostCount} hosts from database", hosts.Count);

            var groups = await _groupRepo.GetAllAsync();
            Groups = new ObservableCollection<HostGroup>(groups);
            _logger.LogInformation("Loaded {GroupCount} groups from database", groups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data from database");
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var hosts = string.IsNullOrWhiteSpace(SearchText)
                ? await _hostRepo.GetAllAsync()
                : await _hostRepo.SearchAsync(SearchText);

            // Check if cancelled before updating UI
            if (cancellationToken.IsCancellationRequested)
                return;

            Hosts = new ObservableCollection<HostEntry>(hosts);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Cancel any pending search
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource?.Dispose();
        _searchCancellationTokenSource = new CancellationTokenSource();

        var cts = _searchCancellationTokenSource;

        // Debounce search with 300ms delay
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await SearchAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when user types quickly - search was cancelled
                _logger.LogDebug("Search cancelled due to new input");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during debounced search");
            }
        });
    }

    [RelayCommand]
    private async Task FilterByGroupAsync(HostGroup? group)
    {
        SelectedGroupFilter = group;
        await RefreshHostsAsync();
    }

    public async Task RefreshHostsAsync()
    {
        IsLoading = true;
        try
        {
            IEnumerable<HostEntry> hosts;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                hosts = await _hostRepo.GetAllAsync();
            }
            else
            {
                hosts = await _hostRepo.SearchAsync(SearchText);
            }

            // Apply group filter if selected
            if (SelectedGroupFilter != null)
            {
                hosts = hosts.Where(h => h.GroupId == SelectedGroupFilter.Id);
            }

            Hosts = new ObservableCollection<HostEntry>(hosts);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync(HostEntry? host)
    {
        if (host == null) return;

        _logger.LogInformation("Initiating connection to {DisplayName} ({Hostname}:{Port}) using {AuthType}",
            host.DisplayName, host.Hostname, host.Port, host.AuthType);

        var settings = await _settingsRepo.GetAsync();

        // Get password - check cache first, then fall back to stored password
        string? password = null;
        bool usedCachedCredential = false;

        if (host.AuthType == AuthType.Password)
        {
            // Try to get cached credential first
            if (settings.EnableCredentialCaching)
            {
                var cachedCredential = _credentialCache.GetCachedCredential(host.Id);
                if (cachedCredential != null && cachedCredential.Type == CredentialType.Password)
                {
                    password = cachedCredential.GetValue();
                    usedCachedCredential = true;
                    _logger.LogDebug("Using cached password for host {DisplayName}", host.DisplayName);
                }
            }

            // Fall back to stored password if no cached credential
            if (password == null && !string.IsNullOrEmpty(host.PasswordProtected))
            {
                password = _secretProtector.TryUnprotect(host.PasswordProtected);
                if (password == null)
                {
                    _logger.LogWarning("Failed to decrypt password for host {DisplayName}", host.DisplayName);
                }
                else
                {
                    _logger.LogDebug("Password decrypted successfully for host {DisplayName}", host.DisplayName);

                    // Cache the credential for future connections if caching is enabled
                    if (settings.EnableCredentialCaching)
                    {
                        CacheCredentialForHost(host.Id, password, CredentialType.Password);
                    }
                }
            }
        }

        // Create session
        var session = _sessionManager.CreateSession(host.DisplayName);
        session.Host = host;
        session.DecryptedPassword = password?.ToSecureString();

        // Re-evaluate SFTP command since Host is now set
        OpenSftpBrowserCommand.NotifyCanExecuteChanged();

        _logger.LogDebug("Terminal session {SessionId} created for host {DisplayName} (cached credential: {UsedCache})",
            session.Id, host.DisplayName, usedCachedCredential);

        // Initialize session logging if enabled
        try
        {
            if (settings.EnableSessionLogging)
            {
                // Apply custom directory if set
                if (!string.IsNullOrEmpty(settings.SessionLogDirectory))
                {
                    _sessionLoggingService.SetLogDirectory(settings.SessionLogDirectory);
                }
                _sessionLoggingService.SetTimestampEachLine(settings.SessionLogTimestampLines);
                _sessionLoggingService.SetMaxLogFileSizeMB(settings.MaxLogFileSizeMB);
                _sessionLoggingService.SetMaxLogFilesToKeep(settings.MaxLogFilesToKeep);

                var sessionTitle = $"{host.DisplayName}_{host.Hostname}";
                var logLevel = ParseSessionLogLevel(settings.SessionLogLevel);
                session.LogLevel = logLevel;
                session.RedactTypedSecrets = settings.RedactTypedSecrets;
                session.SessionLogger = _sessionLoggingService.StartLogging(
                    session.Id,
                    sessionTitle,
                    logLevel,
                    session.RedactTypedSecrets);
                session.SessionLogger.LogEvent("CONNECT", $"Connecting to {host.Hostname}:{host.Port} as {host.Username}");
                _logger.LogDebug("Session logging started for session {SessionId}", session.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize session logging for session {SessionId}", session.Id);
        }

        // Raise SessionCreated event for pane management
        SessionCreated?.Invoke(this, session);
    }

    /// <summary>
    /// Creates a session for a host without raising the SessionCreated event.
    /// Used when the caller wants to manage pane creation manually.
    /// </summary>
    public async Task<TerminalSession?> CreateSessionForHostAsync(HostEntry host)
    {
        _logger.LogInformation("Creating session for {DisplayName} ({Hostname}:{Port}) using {AuthType}",
            host.DisplayName, host.Hostname, host.Port, host.AuthType);

        var settings = await _settingsRepo.GetAsync();

        // Get password - check cache first, then fall back to stored password
        string? password = null;

        if (host.AuthType == AuthType.Password)
        {
            // Try to get cached credential first
            if (settings.EnableCredentialCaching)
            {
                var cachedCredential = _credentialCache.GetCachedCredential(host.Id);
                if (cachedCredential != null && cachedCredential.Type == CredentialType.Password)
                {
                    password = cachedCredential.GetValue();
                    _logger.LogDebug("Using cached password for host {DisplayName}", host.DisplayName);
                }
            }

            // Fall back to stored password if no cached credential
            if (password == null && !string.IsNullOrEmpty(host.PasswordProtected))
            {
                password = _secretProtector.TryUnprotect(host.PasswordProtected);
                if (password != null && settings.EnableCredentialCaching)
                {
                    CacheCredentialForHost(host.Id, password, CredentialType.Password);
                }
            }
        }

        // Create session
        var session = _sessionManager.CreateSession(host.DisplayName);
        session.Host = host;
        session.DecryptedPassword = password?.ToSecureString();

        _logger.LogDebug("Terminal session {SessionId} created for host {DisplayName}", session.Id, host.DisplayName);

        // Initialize session logging if enabled
        try
        {
            if (settings.EnableSessionLogging)
            {
                if (!string.IsNullOrEmpty(settings.SessionLogDirectory))
                {
                    _sessionLoggingService.SetLogDirectory(settings.SessionLogDirectory);
                }
                _sessionLoggingService.SetTimestampEachLine(settings.SessionLogTimestampLines);
                _sessionLoggingService.SetMaxLogFileSizeMB(settings.MaxLogFileSizeMB);
                _sessionLoggingService.SetMaxLogFilesToKeep(settings.MaxLogFilesToKeep);

                var sessionTitle = $"{host.DisplayName}_{host.Hostname}";
                var logLevel = ParseSessionLogLevel(settings.SessionLogLevel);
                session.LogLevel = logLevel;
                session.RedactTypedSecrets = settings.RedactTypedSecrets;
                session.SessionLogger = _sessionLoggingService.StartLogging(
                    session.Id,
                    sessionTitle,
                    logLevel,
                    session.RedactTypedSecrets);
                session.SessionLogger.LogEvent("CONNECT", $"Connecting to {host.Hostname}:{host.Port} as {host.Username}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize session logging for session {SessionId}", session.Id);
        }

        return session;
    }

    [RelayCommand]
    private void CloseSession(TerminalSession? session)
    {
        if (session != null)
        {
            _sessionManager.CloseSession(session.Id);
        }
    }

    /// <summary>
    /// Toggles session logging for the current session.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleSessionLogging))]
    private async Task ToggleSessionLoggingAsync()
    {
        var session = CurrentSession;
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

    private bool CanToggleSessionLogging() => CurrentSession != null;

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
        if (!System.IO.Directory.Exists(logDir))
        {
            System.IO.Directory.CreateDirectory(logDir);
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
    /// Toggles broadcast input mode on/off.
    /// </summary>
    [RelayCommand]
    private void ToggleBroadcastMode()
    {
        IsBroadcastMode = !IsBroadcastMode;
        _logger.LogInformation("Broadcast mode toggled to {State}", IsBroadcastMode);
    }

    /// <summary>
    /// Selects all connected sessions for broadcast input.
    /// </summary>
    [RelayCommand]
    private void SelectAllForBroadcast()
    {
        _sessionManager.SelectAllForBroadcast();
        OnPropertyChanged(nameof(BroadcastSelectedCount));
        _logger.LogDebug("All sessions selected for broadcast");
    }

    /// <summary>
    /// Deselects all sessions from broadcast input.
    /// </summary>
    [RelayCommand]
    private void DeselectAllForBroadcast()
    {
        _sessionManager.DeselectAllForBroadcast();
        OnPropertyChanged(nameof(BroadcastSelectedCount));
        _logger.LogDebug("All sessions deselected from broadcast");
    }

    /// <summary>
    /// Toggles a session's selection for broadcast input.
    /// </summary>
    [RelayCommand]
    private void ToggleBroadcastSelection(TerminalSession? session)
    {
        if (session == null) return;
        _sessionManager.ToggleBroadcastSelection(session);
        OnPropertyChanged(nameof(BroadcastSelectedCount));
    }

    /// <summary>
    /// Gets the broadcast input service for the terminal control.
    /// </summary>
    public IBroadcastInputService BroadcastService => _broadcastService;

    /// <summary>
    /// Opens SFTP browser in a separate window for the current session's host.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenSftpBrowser))]
    private async Task OpenSftpBrowserAsync()
    {
        var session = CurrentSession;
        if (session?.Host == null) return;

        var host = session.Host;
        _logger.LogInformation("Opening SFTP browser window for host {DisplayName}", host.DisplayName);

        try
        {
            // Get password from session if available (convert SecureString to string for API)
            string? password = session.DecryptedPassword?.ToUnsecureString();

            // Create connection info
            var connectionInfo = await CreateConnectionInfoAsync(host, password);

            // Connect SFTP
            var sftpSession = await _sftpService.ConnectAsync(connectionInfo);

            // Create ViewModels
            var sftpBrowserVm = new SftpBrowserViewModel(
                sftpSession,
                host.DisplayName,
                App.GetLogger<SftpBrowserViewModel>(),
                App.GetLogger<LocalFileBrowserViewModel>(),
                App.GetLogger<RemoteFileBrowserViewModel>());

            // Initialize the browsers
            await sftpBrowserVm.InitializeAsync();

            var windowVm = new SftpBrowserWindowViewModel(sftpBrowserVm, host.DisplayName);

            // Create and show window
            var window = new SftpBrowserWindow(windowVm)
            {
                Owner = Application.Current.MainWindow
            };
            window.Show();

            _logger.LogInformation("SFTP browser window opened for {DisplayName}", host.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SFTP browser for host {DisplayName}", host.DisplayName);

            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "SFTP Connection Failed",
                Content = $"Could not connect to SFTP:\n\n{ex.Message}"
            };
            await messageBox.ShowDialogAsync();
        }
    }

    private bool CanOpenSftpBrowser() => CurrentSession?.Host != null;

    partial void OnCurrentSessionChanged(TerminalSession? value)
    {
        OpenSftpBrowserCommand.NotifyCanExecuteChanged();
        ToggleSessionLoggingCommand.NotifyCanExecuteChanged();
        OpenCurrentSessionLogFileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCurrentSessionLogging));
        OnPropertyChanged(nameof(CurrentSessionLogPath));
        OnPropertyChanged(nameof(CurrentSessionLogLevel));
        OnPropertyChanged(nameof(CurrentSessionRedactTypedSecrets));
    }

    /// <summary>
    /// Opens SFTP browser directly for a host from the host list (without requiring an active session).
    /// </summary>
    [RelayCommand]
    private async Task OpenSftpBrowserForHostAsync(HostEntry? host)
    {
        if (host == null) return;

        _logger.LogInformation("Opening SFTP browser window for host {DisplayName} from host list", host.DisplayName);

        try
        {
            // Get password - check cache first, then fall back to stored password
            string? password = null;
            var settings = await _settingsRepo.GetAsync();

            if (host.AuthType == AuthType.Password)
            {
                // Try to get cached credential first
                if (settings.EnableCredentialCaching)
                {
                    var cachedCredential = _credentialCache.GetCachedCredential(host.Id);
                    if (cachedCredential != null && cachedCredential.Type == CredentialType.Password)
                    {
                        password = cachedCredential.GetValue();
                        _logger.LogDebug("Using cached password for SFTP to host {DisplayName}", host.DisplayName);
                    }
                }

                // Fall back to stored password if no cached credential
                if (password == null && !string.IsNullOrEmpty(host.PasswordProtected))
                {
                    password = _secretProtector.TryUnprotect(host.PasswordProtected);
                    if (password == null)
                    {
                        _logger.LogWarning("Failed to decrypt password for SFTP to host {DisplayName}", host.DisplayName);
                    }
                    else
                    {
                        _logger.LogDebug("Password decrypted successfully for SFTP to host {DisplayName}", host.DisplayName);

                        // Cache the credential for future connections if caching is enabled
                        if (settings.EnableCredentialCaching)
                        {
                            CacheCredentialForHost(host.Id, password, CredentialType.Password);
                        }
                    }
                }
            }

            // Create connection info
            var connectionInfo = await CreateConnectionInfoAsync(host, password);

            // Connect SFTP
            var sftpSession = await _sftpService.ConnectAsync(connectionInfo);

            // Create ViewModels
            var sftpBrowserVm = new SftpBrowserViewModel(
                sftpSession,
                host.DisplayName,
                App.GetLogger<SftpBrowserViewModel>(),
                App.GetLogger<LocalFileBrowserViewModel>(),
                App.GetLogger<RemoteFileBrowserViewModel>());

            // Initialize the browsers
            await sftpBrowserVm.InitializeAsync();

            var windowVm = new SftpBrowserWindowViewModel(sftpBrowserVm, host.DisplayName);

            // Create and show window
            var window = new SftpBrowserWindow(windowVm)
            {
                Owner = Application.Current.MainWindow
            };
            window.Show();

            _logger.LogInformation("SFTP browser window opened for {DisplayName} from host list", host.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SFTP browser for host {DisplayName}", host.DisplayName);

            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "SFTP Connection Failed",
                Content = $"Could not connect to SFTP:\n\n{ex.Message}"
            };
            await messageBox.ShowDialogAsync();
        }
    }

    [RelayCommand]
    private async Task AddHostAsync()
    {
        var viewModel = new HostDialogViewModel(
            _secretProtector,
            null,
            Groups,
            _proxyJumpRepo,
            _portForwardingRepo);

        // Load proxy jump profiles asynchronously
        await viewModel.LoadProxyJumpProfilesAsync();

        var dialog = new HostEditDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var host = viewModel.GetHost();
            await _hostRepo.AddAsync(host);
            Hosts.Add(host);
            SelectedHost = host;
        }
    }

    [RelayCommand]
    private async Task EditHostAsync(HostEntry? host)
    {
        if (host == null) return;

        var viewModel = new HostDialogViewModel(
            _secretProtector,
            host,
            Groups,
            _proxyJumpRepo,
            _portForwardingRepo);

        // Load proxy jump profiles and port forwarding count asynchronously
        await Task.WhenAll(
            viewModel.LoadProxyJumpProfilesAsync(),
            viewModel.LoadPortForwardingCountAsync());

        var dialog = new HostEditDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var updatedHost = viewModel.GetHost();
            await _hostRepo.UpdateAsync(updatedHost);

            // Refresh the host in the list
            var index = Hosts.IndexOf(host);
            if (index >= 0)
            {
                Hosts[index] = updatedHost;
            }
        }
    }

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        var viewModel = new GroupDialogViewModel(null);
        var dialog = new GroupDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var group = viewModel.GetGroup();
            await _groupRepo.AddAsync(group);
            Groups.Add(group);
        }
    }

    [RelayCommand]
    private async Task EditGroupAsync(HostGroup? group)
    {
        if (group == null) return;

        var viewModel = new GroupDialogViewModel(group);
        var dialog = new GroupDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var updatedGroup = viewModel.GetGroup();
            await _groupRepo.UpdateAsync(updatedGroup);

            // Refresh the group in the list
            var index = Groups.IndexOf(group);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(HostGroup? group)
    {
        if (group == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the group '{group.Name}'?\n\nHosts in this group will be moved to 'Ungrouped'.",
            "Delete Group",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _groupRepo.DeleteAsync(group.Id);
            Groups.Remove(group);

            // Update hosts that belonged to this group
            foreach (var host in Hosts.Where(h => h.GroupId == group.Id))
            {
                host.GroupId = null;
                host.Group = null;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteHostAsync(HostEntry? host)
    {
        if (host == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the host '{host.DisplayName}'?\n\nThis will also delete all connection history for this host.",
            "Delete Host",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await _hostRepo.DeleteAsync(host.Id);
        Hosts.Remove(host);

        if (SelectedHost == host)
            SelectedHost = null;
    }

    [RelayCommand]
    private async Task SaveHostAsync(HostEntry? host)
    {
        if (host == null) return;

        await _hostRepo.UpdateAsync(host);
    }

    public async Task ExportHostsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export SSH Hosts",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            FileName = $"sshmanager-export-{DateTime.Now:yyyy-MM-dd}"
        };

        if (dialog.ShowDialog() == true)
        {
            _logger.LogInformation("Exporting hosts to {FilePath}", dialog.FileName);
            try
            {
                await _exportImportService.ExportAsync(dialog.FileName, Hosts, Groups);
                _logger.LogInformation("Successfully exported {HostCount} hosts and {GroupCount} groups to {FilePath}",
                    Hosts.Count, Groups.Count, dialog.FileName);
                MessageBox.Show(
                    $"Successfully exported {Hosts.Count} hosts and {Groups.Count} groups.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export hosts to {FilePath}", dialog.FileName);
                MessageBox.Show(
                    $"Failed to export: {ex.Message}\n\nCheck logs for details.",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    public async Task ImportHostsAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import SSH Hosts",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _logger.LogInformation("Importing hosts from {FilePath}", dialog.FileName);
            try
            {
                var (hosts, groups) = await _exportImportService.ImportAsync(dialog.FileName);
                _logger.LogDebug("Parsed {HostCount} hosts and {GroupCount} groups from import file", hosts.Count, groups.Count);

                var result = MessageBox.Show(
                    $"Import will add {hosts.Count} hosts and {groups.Count} groups.\n\n" +
                    "Note: Passwords are not imported for security reasons.\n" +
                    "You will need to re-enter passwords for hosts that use password authentication.\n\n" +
                    "Do you want to continue?",
                    "Confirm Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Add groups first
                    foreach (var group in groups)
                    {
                        await _groupRepo.AddAsync(group);
                        Groups.Add(group);
                    }
                    _logger.LogDebug("Added {GroupCount} groups to database", groups.Count);

                    // Then add hosts
                    foreach (var host in hosts)
                    {
                        await _hostRepo.AddAsync(host);
                        Hosts.Add(host);
                    }
                    _logger.LogDebug("Added {HostCount} hosts to database", hosts.Count);

                    _logger.LogInformation("Successfully imported {HostCount} hosts and {GroupCount} groups from {FilePath}",
                        hosts.Count, groups.Count, dialog.FileName);
                    MessageBox.Show(
                        $"Successfully imported {hosts.Count} hosts and {groups.Count} groups.",
                        "Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _logger.LogInformation("Import cancelled by user");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import hosts from {FilePath}", dialog.FileName);
                MessageBox.Show(
                    $"Failed to import: {ex.Message}\n\nCheck logs for details.",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    public ISshConnectionService SshService => _sshService;

    public IHostFingerprintRepository FingerprintRepository => _fingerprintRepo;

    public async Task<TerminalConnectionInfo> CreateConnectionInfoAsync(HostEntry host, string? password)
    {
        var settings = await _settingsRepo.GetAsync();

        var timeoutSeconds = settings.ConnectionTimeoutSeconds > 0
            ? settings.ConnectionTimeoutSeconds
            : 30;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        TimeSpan? keepAlive = null;
        if (settings.KeepAliveIntervalSeconds > 0)
        {
            keepAlive = TimeSpan.FromSeconds(settings.KeepAliveIntervalSeconds);
        }

        return TerminalConnectionInfo.FromHostEntry(host, password, timeout, keepAlive);
    }

    public async Task RecordConnectionResultAsync(
        HostEntry host,
        bool wasSuccessful,
        string? errorMessage,
        DateTimeOffset? connectedAt = null)
    {
        try
        {
            var trimmedError = errorMessage;
            if (!string.IsNullOrEmpty(trimmedError) && trimmedError.Length > 1000)
            {
                trimmedError = trimmedError[..1000];
            }

            await _historyRepo.AddAsync(new ConnectionHistory
            {
                HostId = host.Id,
                ConnectedAt = connectedAt ?? DateTimeOffset.UtcNow,
                WasSuccessful = wasSuccessful,
                ErrorMessage = wasSuccessful ? null : trimmedError
            });

            _logger.LogDebug("Connection history recorded for host {HostId} (success: {WasSuccessful})",
                host.Id, wasSuccessful);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record connection history for host {HostId}", host.Id);
        }
    }

    private static SessionLogLevel ParseSessionLogLevel(string? value)
    {
        return Enum.TryParse(value, true, out SessionLogLevel parsed)
            ? parsed
            : SessionLogLevel.OutputAndEvents;
    }

    /// <summary>
    /// Creates a keyboard-interactive authentication callback that shows a dialog for 2FA/TOTP prompts.
    /// </summary>
    public KeyboardInteractiveCallback CreateKeyboardInteractiveCallback()
    {
        return async (request) =>
        {
            _logger.LogDebug("Received keyboard-interactive request: {Name} with {PromptCount} prompts",
                request.Name, request.Prompts.Count);

            // Show dialog on UI thread
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new KeyboardInteractiveDialog();
                dialog.Owner = Application.Current.MainWindow;
                dialog.Initialize(request);
                dialog.ShowDialog();
                return dialog.GetResponseRequest();
            });

            if (result != null)
            {
                _logger.LogDebug("User provided responses to {PromptCount} prompts", result.Prompts.Count);
            }
            else
            {
                _logger.LogInformation("User cancelled keyboard-interactive authentication");
            }

            return result;
        };
    }

    /// <summary>
    /// Creates a host key verification callback for the specified host.
    /// </summary>
    public HostKeyVerificationCallback CreateHostKeyVerificationCallback(Guid hostId)
    {
        return async (hostname, port, algorithm, fingerprint, keyBytes) =>
        {
            _logger.LogDebug("Verifying host key for {Hostname}:{Port} - {Algorithm}", hostname, port, algorithm);

            // Get existing fingerprint from database
            var existingFingerprint = await _fingerprintRepo.GetByHostAsync(hostId);

            // Check if fingerprint matches
            if (existingFingerprint != null && existingFingerprint.Fingerprint == fingerprint)
            {
                // Fingerprint matches - update last seen and trust
                await _fingerprintRepo.UpdateLastSeenAsync(existingFingerprint.Id);
                _logger.LogDebug("Host key verified - fingerprint matches stored value");
                return true;
            }

            // Show verification dialog on UI thread
            var accepted = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new HostKeyVerificationDialog();
                dialog.Owner = Application.Current.MainWindow;
                dialog.Initialize(hostname, port, algorithm, fingerprint, existingFingerprint);
                dialog.ShowDialog();
                return dialog.IsAccepted;
            });

            if (accepted)
            {
                if (existingFingerprint != null)
                {
                    // Update existing fingerprint
                    existingFingerprint.Algorithm = algorithm;
                    existingFingerprint.Fingerprint = fingerprint;
                    existingFingerprint.LastSeen = DateTimeOffset.UtcNow;
                    existingFingerprint.IsTrusted = true;
                    await _fingerprintRepo.UpdateAsync(existingFingerprint);
                    _logger.LogInformation("Updated host key fingerprint for {Hostname}:{Port}", hostname, port);
                }
                else
                {
                    // Add new fingerprint
                    var newFingerprint = new HostFingerprint
                    {
                        HostId = hostId,
                        Algorithm = algorithm,
                        Fingerprint = fingerprint,
                        FirstSeen = DateTimeOffset.UtcNow,
                        LastSeen = DateTimeOffset.UtcNow,
                        IsTrusted = true
                    };
                    await _fingerprintRepo.AddAsync(newFingerprint);
                    _logger.LogInformation("Stored new host key fingerprint for {Hostname}:{Port}", hostname, port);
                }
            }
            else
            {
                _logger.LogWarning("Host key rejected by user for {Hostname}:{Port}", hostname, port);
            }

            return accepted;
        };
    }

    public async Task ImportFromSshConfigAsync()
    {
        _logger.LogInformation("Opening SSH config import dialog");

        var dialog = new SshConfigImportDialog();
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var importItems = dialog.GetSelectedImportItems();
            if (importItems.Count == 0)
            {
                _logger.LogInformation("No hosts selected for import");
                return;
            }

            _logger.LogInformation("Importing {HostCount} hosts from SSH config", importItems.Count);

            try
            {
                int hostsImported = 0;
                int proxyJumpProfilesCreated = 0;
                int portForwardingProfilesCreated = 0;

                // Build a mapping from alias to host entry for ProxyJump resolution
                var aliasToHost = new Dictionary<string, HostEntry>(StringComparer.OrdinalIgnoreCase);

                // First pass: Import all hosts and build alias mapping
                foreach (var item in importItems)
                {
                    await _hostRepo.AddAsync(item.HostEntry);
                    Hosts.Add(item.HostEntry);
                    aliasToHost[item.ConfigHost.Alias] = item.HostEntry;
                    hostsImported++;
                }

                // Second pass: Create port forwarding profiles for hosts with forwarding config
                foreach (var item in importItems)
                {
                    var configHost = item.ConfigHost;
                    var host = item.HostEntry;

                    // Create LocalForward profiles
                    foreach (var lf in configHost.LocalForwards)
                    {
                        var profile = new PortForwardingProfile
                        {
                            DisplayName = $"L:{lf.LocalPort}→{lf.RemoteHost}:{lf.RemotePort}",
                            Description = $"Imported from SSH config - Local forward",
                            ForwardingType = PortForwardingType.LocalForward,
                            LocalBindAddress = lf.BindAddress,
                            LocalPort = lf.LocalPort,
                            RemoteHost = lf.RemoteHost,
                            RemotePort = lf.RemotePort,
                            HostId = host.Id,
                            IsEnabled = true,
                            AutoStart = false
                        };

                        await _portForwardingRepo.AddAsync(profile);
                        portForwardingProfilesCreated++;
                        _logger.LogDebug("Created LocalForward profile for host {HostName}: {ProfileName}",
                            host.DisplayName, profile.DisplayName);
                    }

                    // Create RemoteForward profiles
                    foreach (var rf in configHost.RemoteForwards)
                    {
                        var profile = new PortForwardingProfile
                        {
                            DisplayName = $"R:{rf.RemotePort}→{rf.LocalHost}:{rf.LocalPort}",
                            Description = $"Imported from SSH config - Remote forward",
                            ForwardingType = PortForwardingType.RemoteForward,
                            LocalBindAddress = rf.BindAddress,
                            LocalPort = rf.LocalPort,
                            RemoteHost = rf.LocalHost,
                            RemotePort = rf.RemotePort,
                            HostId = host.Id,
                            IsEnabled = true,
                            AutoStart = false
                        };

                        await _portForwardingRepo.AddAsync(profile);
                        portForwardingProfilesCreated++;
                        _logger.LogDebug("Created RemoteForward profile for host {HostName}: {ProfileName}",
                            host.DisplayName, profile.DisplayName);
                    }

                    // Create DynamicForward profiles (SOCKS proxy)
                    foreach (var df in configHost.DynamicForwards)
                    {
                        var profile = new PortForwardingProfile
                        {
                            DisplayName = $"D:{df.Port} (SOCKS)",
                            Description = $"Imported from SSH config - SOCKS proxy",
                            ForwardingType = PortForwardingType.DynamicForward,
                            LocalBindAddress = df.BindAddress,
                            LocalPort = df.Port,
                            RemoteHost = null,
                            RemotePort = null,
                            HostId = host.Id,
                            IsEnabled = true,
                            AutoStart = false
                        };

                        await _portForwardingRepo.AddAsync(profile);
                        portForwardingProfilesCreated++;
                        _logger.LogDebug("Created DynamicForward profile for host {HostName}: {ProfileName}",
                            host.DisplayName, profile.DisplayName);
                    }
                }

                // Third pass: Handle ProxyJump configurations
                foreach (var item in importItems)
                {
                    if (string.IsNullOrEmpty(item.ConfigHost.ProxyJump))
                        continue;

                    var host = item.HostEntry;
                    var proxyJumpValue = item.ConfigHost.ProxyJump;

                    // ProxyJump can be comma-separated list of jump hosts
                    var jumpHosts = proxyJumpValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    // Try to resolve all jump hosts
                    var resolvedHops = new List<HostEntry>();
                    var allResolved = true;

                    foreach (var jumpAlias in jumpHosts)
                    {
                        // First check if it's in our just-imported hosts
                        if (aliasToHost.TryGetValue(jumpAlias, out var jumpHost))
                        {
                            resolvedHops.Add(jumpHost);
                        }
                        // Check if it exists in the database already (by display name)
                        else
                        {
                            var existingHost = Hosts.FirstOrDefault(h =>
                                h.DisplayName.Equals(jumpAlias, StringComparison.OrdinalIgnoreCase));

                            if (existingHost != null)
                            {
                                resolvedHops.Add(existingHost);
                            }
                            else
                            {
                                _logger.LogWarning("ProxyJump host '{JumpAlias}' for '{HostName}' not found - skipping profile creation",
                                    jumpAlias, host.DisplayName);
                                allResolved = false;
                                break;
                            }
                        }
                    }

                    if (allResolved && resolvedHops.Count > 0)
                    {
                        // Create the ProxyJump profile
                        var profileName = resolvedHops.Count == 1
                            ? $"Jump via {resolvedHops[0].DisplayName}"
                            : $"Jump chain: {string.Join(" → ", resolvedHops.Select(h => h.DisplayName))}";

                        var proxyProfile = new ProxyJumpProfile
                        {
                            DisplayName = profileName,
                            Description = $"Imported from SSH config for {host.DisplayName}",
                            IsEnabled = true
                        };

                        // Add the hops
                        for (int i = 0; i < resolvedHops.Count; i++)
                        {
                            proxyProfile.JumpHops.Add(new ProxyJumpHop
                            {
                                JumpHostId = resolvedHops[i].Id,
                                SortOrder = i
                            });
                        }

                        var savedProfile = await _proxyJumpRepo.AddAsync(proxyProfile);
                        proxyJumpProfilesCreated++;

                        // Associate the profile with the target host
                        host.ProxyJumpProfileId = savedProfile.Id;
                        await _hostRepo.UpdateAsync(host);

                        _logger.LogDebug("Created ProxyJump profile '{ProfileName}' for host {HostName}",
                            profileName, host.DisplayName);
                    }
                    else if (!allResolved)
                    {
                        // Add a note to the host about unresolved ProxyJump
                        var note = host.Notes ?? "";
                        if (!string.IsNullOrEmpty(note))
                            note += "\n\n";
                        note += $"Note: ProxyJump '{proxyJumpValue}' could not be fully resolved during import.";
                        host.Notes = note;
                        await _hostRepo.UpdateAsync(host);
                    }
                }

                _logger.LogInformation(
                    "Successfully imported {HostCount} hosts, {ProxyCount} proxy profiles, {ForwardCount} port forwarding profiles from SSH config",
                    hostsImported, proxyJumpProfilesCreated, portForwardingProfilesCreated);

                var message = $"Successfully imported {hostsImported} host(s) from SSH config.";
                if (proxyJumpProfilesCreated > 0 || portForwardingProfilesCreated > 0)
                {
                    message += $"\n\nAlso created:";
                    if (proxyJumpProfilesCreated > 0)
                        message += $"\n• {proxyJumpProfilesCreated} ProxyJump profile(s)";
                    if (portForwardingProfilesCreated > 0)
                        message += $"\n• {portForwardingProfilesCreated} port forwarding profile(s)";
                }

                MessageBox.Show(
                    message,
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import hosts from SSH config");
                MessageBox.Show(
                    $"Failed to import: {ex.Message}\n\nCheck logs for details.",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        else
        {
            _logger.LogInformation("SSH config import cancelled by user");
        }
    }

    public async Task ImportFromPuttyAsync()
    {
        _logger.LogInformation("Opening PuTTY import dialog");

        var dialog = new PuttyImportDialog();
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var hosts = dialog.GetSelectedHosts();
            if (hosts.Count == 0)
            {
                _logger.LogInformation("No PuTTY sessions selected for import");
                return;
            }

            _logger.LogInformation("Importing {HostCount} hosts from PuTTY", hosts.Count);

            try
            {
                foreach (var host in hosts)
                {
                    await _hostRepo.AddAsync(host);
                    Hosts.Add(host);
                }

                _logger.LogInformation("Successfully imported {HostCount} hosts from PuTTY", hosts.Count);
                MessageBox.Show(
                    $"Successfully imported {hosts.Count} host(s) from PuTTY.",
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import hosts from PuTTY");
                MessageBox.Show(
                    $"Failed to import: {ex.Message}\n\nCheck logs for details.",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        else
        {
            _logger.LogInformation("PuTTY import cancelled by user");
        }
    }

    /// <summary>
    /// Caches a credential for the specified host using the configured cache timeout.
    /// </summary>
    private void CacheCredentialForHost(Guid hostId, string value, CredentialType type)
    {
        try
        {
            if (_credentialCache is SecureCredentialCache secureCache)
            {
                var credential = secureCache.CreateCredential(type, value);
                _credentialCache.CacheCredential(hostId, credential);
                _logger.LogDebug("Cached {CredentialType} credential for host {HostId}", type, hostId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache credential for host {HostId}", hostId);
        }
    }

    /// <summary>
    /// Gets the credential cache for external access (e.g., from terminal controls).
    /// </summary>
    public ICredentialCache CredentialCache => _credentialCache;

    /// <summary>
    /// Gets the ProxyJump profile repository for external access.
    /// </summary>
    public IProxyJumpProfileRepository ProxyJumpProfileRepository => _proxyJumpRepo;

    /// <summary>
    /// Gets the port forwarding profile repository for external access.
    /// </summary>
    public IPortForwardingProfileRepository PortForwardingProfileRepository => _portForwardingRepo;

    /// <summary>
    /// Gets the host repository for external access.
    /// </summary>
    public IHostRepository HostRepository => _hostRepo;
}
