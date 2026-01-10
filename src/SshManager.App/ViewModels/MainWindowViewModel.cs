using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using IBroadcastInputService = SshManager.Terminal.Services.IBroadcastInputService;
using SshManager.App.Services;

namespace SshManager.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly HostManagementViewModel _hostManagement;
    private readonly SessionViewModel _session;
    private readonly SessionLoggingViewModel _sessionLogging;
    private readonly BroadcastInputViewModel _broadcastInput;
    private readonly SftpLauncherViewModel _sftpLauncher;
    private readonly ImportExportViewModel _importExport;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ISecretProtector _secretProtector;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly IHostStatusService _hostStatusService;
    private readonly ITerminalResizeService _terminalResizeService;
    private readonly ILogger<MainWindowViewModel> _logger;

    // Facade properties delegating to HostManagementViewModel
    public ObservableCollection<HostEntry> Hosts => _hostManagement.Hosts;
    public ObservableCollection<HostGroup> Groups => _hostManagement.Groups;

    public HostEntry? SelectedHost
    {
        get => _hostManagement.SelectedHost;
        set => _hostManagement.SelectedHost = value;
    }

    public string SearchText
    {
        get => _hostManagement.SearchText;
        set => _hostManagement.SearchText = value;
    }

    public bool IsLoading => _hostManagement.IsLoading;

    public HostGroup? SelectedGroupFilter
    {
        get => _hostManagement.SelectedGroupFilter;
        set => _hostManagement.SelectedGroupFilter = value;
    }

    // Facade commands delegating to HostManagementViewModel
    public IAsyncRelayCommand AddHostCommand => _hostManagement.AddHostCommand;
    public IAsyncRelayCommand<HostEntry?> EditHostCommand => _hostManagement.EditHostCommand;
    public IAsyncRelayCommand<HostEntry?> DeleteHostCommand => _hostManagement.DeleteHostCommand;
    public IAsyncRelayCommand<HostEntry?> SaveHostCommand => _hostManagement.SaveHostCommand;
    public IAsyncRelayCommand AddGroupCommand => _hostManagement.AddGroupCommand;
    public IAsyncRelayCommand<HostGroup?> EditGroupCommand => _hostManagement.EditGroupCommand;
    public IAsyncRelayCommand<HostGroup?> DeleteGroupCommand => _hostManagement.DeleteGroupCommand;
    public IAsyncRelayCommand SearchCommand => _hostManagement.SearchCommand;
    public IAsyncRelayCommand<HostGroup?> FilterByGroupCommand => _hostManagement.FilterByGroupCommand;
    public IAsyncRelayCommand<Behaviors.DragDropReorderEventArgs> ReorderHostsCommand => _hostManagement.ReorderHostsCommand;

    // Facade properties delegating to SessionViewModel
    public ObservableCollection<TerminalSession> Sessions => _session.Sessions;

    public TerminalSession? CurrentSession
    {
        get => _session.CurrentSession;
        set => _session.CurrentSession = value;
    }

    // Facade commands delegating to SessionViewModel
    public IAsyncRelayCommand<HostEntry?> ConnectCommand => _session.ConnectCommand;
    public IRelayCommand<TerminalSession?> CloseSessionCommand => _session.CloseSessionCommand;

    public bool HasActiveSessions => _session.HasActiveSessions;

    /// <summary>
    /// Gets host statuses for UI binding.
    /// </summary>
    public IReadOnlyDictionary<Guid, HostStatus> HostStatuses => _hostStatusService.GetAllStatuses();

    // Facade properties delegating to SessionLoggingViewModel
    public bool IsCurrentSessionLogging => _sessionLogging.IsCurrentSessionLogging;
    public string? CurrentSessionLogPath => _sessionLogging.CurrentSessionLogPath;
    public IReadOnlyList<SessionLogLevel> AvailableSessionLogLevels => _sessionLogging.AvailableSessionLogLevels;

    public SessionLogLevel CurrentSessionLogLevel
    {
        get => _sessionLogging.CurrentSessionLogLevel;
        set => _sessionLogging.CurrentSessionLogLevel = value;
    }

    public bool CurrentSessionRedactTypedSecrets
    {
        get => _sessionLogging.CurrentSessionRedactTypedSecrets;
        set => _sessionLogging.CurrentSessionRedactTypedSecrets = value;
    }

    // Facade commands delegating to SessionLoggingViewModel
    public IRelayCommand<SessionLogLevel> SetCurrentSessionLogLevelCommand => _sessionLogging.SetCurrentSessionLogLevelCommand;
    public IAsyncRelayCommand ToggleSessionLoggingCommand => _sessionLogging.ToggleSessionLoggingCommand;
    public IRelayCommand OpenCurrentSessionLogFileCommand => _sessionLogging.OpenCurrentSessionLogFileCommand;
    public IRelayCommand OpenLogDirectoryCommand => _sessionLogging.OpenLogDirectoryCommand;

    // Facade properties delegating to BroadcastInputViewModel
    /// <summary>
    /// Whether broadcast input mode is enabled.
    /// </summary>
    public bool IsBroadcastMode
    {
        get => _broadcastInput.IsBroadcastMode;
        set => _broadcastInput.IsBroadcastMode = value;
    }

    /// <summary>
    /// Number of sessions selected for broadcast.
    /// </summary>
    public int BroadcastSelectedCount => _broadcastInput.BroadcastSelectedCount;

    // Facade commands delegating to BroadcastInputViewModel
    public IRelayCommand ToggleBroadcastModeCommand => _broadcastInput.ToggleBroadcastModeCommand;
    public IRelayCommand SelectAllForBroadcastCommand => _broadcastInput.SelectAllForBroadcastCommand;
    public IRelayCommand DeselectAllForBroadcastCommand => _broadcastInput.DeselectAllForBroadcastCommand;
    public IRelayCommand<TerminalSession?> ToggleBroadcastSelectionCommand => _broadcastInput.ToggleBroadcastSelectionCommand;

    // Facade commands delegating to SftpLauncherViewModel
    public IAsyncRelayCommand OpenSftpBrowserCommand => _sftpLauncher.OpenSftpBrowserCommand;
    public IAsyncRelayCommand<HostEntry?> OpenSftpBrowserForHostCommand => _sftpLauncher.OpenSftpBrowserForHostCommand;

    /// <summary>
    /// Port forwarding manager for managing active port forwards.
    /// </summary>
    public PortForwardingManagerViewModel PortForwardingManager { get; }

    /// <summary>
    /// Event raised when a new session is created (for pane management).
    /// Delegates to SessionViewModel.
    /// </summary>
    public event EventHandler<TerminalSession>? SessionCreated
    {
        add => _session.SessionCreated += value;
        remove => _session.SessionCreated -= value;
    }

    public MainWindowViewModel(
        HostManagementViewModel hostManagement,
        SessionViewModel session,
        SessionLoggingViewModel sessionLogging,
        BroadcastInputViewModel broadcastInput,
        SftpLauncherViewModel sftpLauncher,
        ImportExportViewModel importExport,
        ISettingsRepository settingsRepo,
        ISecretProtector secretProtector,
        ITerminalSessionManager sessionManager,
        IHostStatusService hostStatusService,
        ITerminalResizeService terminalResizeService,
        PortForwardingManagerViewModel portForwardingManager,
        ILogger<MainWindowViewModel>? logger = null)
    {
        _hostManagement = hostManagement;
        _session = session;
        _sessionLogging = sessionLogging;
        _broadcastInput = broadcastInput;
        _sftpLauncher = sftpLauncher;
        _importExport = importExport;
        _settingsRepo = settingsRepo;
        _secretProtector = secretProtector;
        _sessionManager = sessionManager;
        _hostStatusService = hostStatusService;
        _terminalResizeService = terminalResizeService;
        PortForwardingManager = portForwardingManager;
        _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;

        _logger.LogDebug("MainWindowViewModel initializing");

        // Forward property changes from HostManagementViewModel
        _hostManagement.PropertyChanged += OnHostManagementPropertyChanged;

        // Forward property changes from SessionViewModel
        _session.PropertyChanged += OnSessionPropertyChanged;

        // Forward property changes from SessionLoggingViewModel
        _sessionLogging.PropertyChanged += OnSessionLoggingPropertyChanged;

        // Forward property changes from BroadcastInputViewModel
        _broadcastInput.PropertyChanged += OnBroadcastInputPropertyChanged;

        // Startup validation: warn if terminal resize is not available
        if (!_terminalResizeService.IsResizeSupported)
        {
            _logger.LogWarning(
                "Terminal resize is not available - terminal windows may not resize properly. " +
                "This may be due to an SSH.NET library version change.");
        }

        // Subscribe to host status changes
        _hostStatusService.StatusChanged += OnHostStatusChanged;

        _logger.LogDebug("MainWindowViewModel initialized");
    }

    private void OnHostManagementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Forward property change notifications for facade properties
        switch (e.PropertyName)
        {
            case nameof(HostManagementViewModel.Hosts):
                OnPropertyChanged(nameof(Hosts));
                break;
            case nameof(HostManagementViewModel.Groups):
                OnPropertyChanged(nameof(Groups));
                break;
            case nameof(HostManagementViewModel.SelectedHost):
                OnPropertyChanged(nameof(SelectedHost));
                break;
            case nameof(HostManagementViewModel.SearchText):
                OnPropertyChanged(nameof(SearchText));
                break;
            case nameof(HostManagementViewModel.IsLoading):
                OnPropertyChanged(nameof(IsLoading));
                break;
            case nameof(HostManagementViewModel.SelectedGroupFilter):
                OnPropertyChanged(nameof(SelectedGroupFilter));
                break;
        }
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionViewModel.Sessions):
                OnPropertyChanged(nameof(Sessions));
                break;
            case nameof(SessionViewModel.CurrentSession):
                OnPropertyChanged(nameof(CurrentSession));
                break;
            case nameof(SessionViewModel.HasActiveSessions):
                OnPropertyChanged(nameof(HasActiveSessions));
                break;
        }
    }

    private void OnSessionLoggingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionLoggingViewModel.IsCurrentSessionLogging):
                OnPropertyChanged(nameof(IsCurrentSessionLogging));
                break;
            case nameof(SessionLoggingViewModel.CurrentSessionLogPath):
                OnPropertyChanged(nameof(CurrentSessionLogPath));
                break;
            case nameof(SessionLoggingViewModel.CurrentSessionLogLevel):
                OnPropertyChanged(nameof(CurrentSessionLogLevel));
                break;
            case nameof(SessionLoggingViewModel.CurrentSessionRedactTypedSecrets):
                OnPropertyChanged(nameof(CurrentSessionRedactTypedSecrets));
                break;
        }
    }

    private void OnBroadcastInputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BroadcastInputViewModel.IsBroadcastMode):
                OnPropertyChanged(nameof(IsBroadcastMode));
                break;
            case nameof(BroadcastInputViewModel.BroadcastSelectedCount):
                OnPropertyChanged(nameof(BroadcastSelectedCount));
                break;
        }
    }

    private void OnHostStatusChanged(object? sender, EventArgs e)
    {
        // Notify that HostStatuses has changed (UI will re-query)
        OnPropertyChanged(nameof(HostStatuses));
    }

    /// <summary>
    /// Loads hosts and groups from the database.
    /// Delegates to HostManagementViewModel.
    /// </summary>
    public Task LoadDataAsync() => _hostManagement.LoadDataAsync();

    /// <summary>
    /// Refreshes the hosts list based on current search text and group filter.
    /// Delegates to HostManagementViewModel.
    /// </summary>
    public Task RefreshHostsAsync() => _hostManagement.RefreshHostsAsync();

    /// <summary>
    /// Creates a session for a host without raising the SessionCreated event.
    /// Used when the caller wants to manage pane creation manually.
    /// Delegates to SessionViewModel.
    /// </summary>
    public Task<TerminalSession?> CreateSessionForHostAsync(HostEntry host) =>
        _session.CreateSessionForHostAsync(host);

    /// <summary>
    /// Gets the broadcast input service for the terminal control.
    /// Delegates to BroadcastInputViewModel.
    /// </summary>
    public IBroadcastInputService BroadcastService => _broadcastInput.BroadcastService;

    /// <summary>
    /// Exports all hosts and groups to a JSON file.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ExportHostsAsync() => _importExport.ExportHostsAsync();

    /// <summary>
    /// Imports hosts and groups from a JSON file.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ImportHostsAsync() => _importExport.ImportHostsAsync();

    /// <summary>
    /// Gets the SSH connection service for external access.
    /// Delegates to SessionViewModel.
    /// </summary>
    public ISshConnectionService SshService => _session.SshService;

    /// <summary>
    /// Gets the host fingerprint repository for external access.
    /// Delegates to SessionViewModel.
    /// </summary>
    public IHostFingerprintRepository FingerprintRepository => _session.FingerprintRepository;

    /// <summary>
    /// Creates connection info from a host entry and optional password.
    /// Delegates to SessionViewModel.
    /// </summary>
    public Task<TerminalConnectionInfo> CreateConnectionInfoAsync(HostEntry host, string? password) =>
        _session.CreateConnectionInfoAsync(host, password);

    /// <summary>
    /// Records a connection result in the connection history.
    /// Delegates to SessionViewModel.
    /// </summary>
    public Task RecordConnectionResultAsync(
        HostEntry host,
        bool wasSuccessful,
        string? errorMessage,
        DateTimeOffset? connectedAt = null) =>
        _session.RecordConnectionResultAsync(host, wasSuccessful, errorMessage, connectedAt);

    /// <summary>
    /// Creates a keyboard-interactive authentication callback that shows a dialog for 2FA/TOTP prompts.
    /// Delegates to SessionViewModel.
    /// </summary>
    public KeyboardInteractiveCallback CreateKeyboardInteractiveCallback() =>
        _session.CreateKeyboardInteractiveCallback();

    /// <summary>
    /// Creates a host key verification callback for the specified host.
    /// Delegates to SessionViewModel.
    /// </summary>
    public HostKeyVerificationCallback CreateHostKeyVerificationCallback(Guid hostId) =>
        _session.CreateHostKeyVerificationCallback(hostId);

    /// <summary>
    /// Imports hosts from an SSH config file (~/.ssh/config).
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ImportFromSshConfigAsync() => _importExport.ImportFromSshConfigAsync();

    /// <summary>
    /// Imports hosts from PuTTY sessions stored in the Windows registry.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ImportFromPuttyAsync() => _importExport.ImportFromPuttyAsync();

    /// <summary>
    /// Event raised when hosts have been imported.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public event EventHandler? HostsImported
    {
        add => _importExport.HostsImported += value;
        remove => _importExport.HostsImported -= value;
    }

    /// <summary>
    /// Gets the credential cache for external access (e.g., from terminal controls).
    /// Delegates to SessionViewModel.
    /// </summary>
    public ICredentialCache CredentialCache => _session.CredentialCache;

    /// <summary>
    /// Gets the ProxyJump profile repository for external access.
    /// </summary>
    public IProxyJumpProfileRepository ProxyJumpProfileRepository => _hostManagement.ProxyJumpProfileRepository;

    /// <summary>
    /// Gets the port forwarding profile repository for external access.
    /// </summary>
    public IPortForwardingProfileRepository PortForwardingProfileRepository => _hostManagement.PortForwardingProfileRepository;

    /// <summary>
    /// Gets the host repository for external access.
    /// </summary>
    public IHostRepository HostRepository => _hostManagement.HostRepository;

    public void Dispose()
    {
        _hostManagement.PropertyChanged -= OnHostManagementPropertyChanged;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _sessionLogging.PropertyChanged -= OnSessionLoggingPropertyChanged;
        _broadcastInput.PropertyChanged -= OnBroadcastInputPropertyChanged;
        _hostStatusService.StatusChanged -= OnHostStatusChanged;
    }
}
