using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.App.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// Main window view model that coordinates sub-ViewModels for different functional areas.
/// Sub-VMs are exposed directly for XAML binding (e.g., {Binding HostManagement.Hosts}).
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IHostStatusService _hostStatusService;
    private readonly ITerminalResizeService _terminalResizeService;
    private readonly ILogger<MainWindowViewModel> _logger;

    /// <summary>
    /// Host and group management (CRUD, search, filtering).
    /// </summary>
    public HostManagementViewModel HostManagement { get; }

    /// <summary>
    /// Terminal session lifecycle and SSH connections.
    /// </summary>
    public SessionViewModel Session { get; }

    /// <summary>
    /// Session logging controls.
    /// </summary>
    public SessionLoggingViewModel SessionLogging { get; }

    /// <summary>
    /// Multi-session broadcast input functionality.
    /// </summary>
    public BroadcastInputViewModel BroadcastInput { get; }

    /// <summary>
    /// SFTP browser launching.
    /// </summary>
    public SftpLauncherViewModel SftpLauncher { get; }

    /// <summary>
    /// Import/export functionality.
    /// </summary>
    public ImportExportViewModel ImportExport { get; }

    /// <summary>
    /// Port forwarding manager for managing active port forwards.
    /// </summary>
    public PortForwardingManagerViewModel PortForwardingManager { get; }

    /// <summary>
    /// Quick connect overlay view model for Ctrl+K host search.
    /// </summary>
    public QuickConnectOverlayViewModel QuickConnectOverlay { get; }

    [ObservableProperty]
    private bool _isLoadingHosts = true;

    /// <summary>
    /// Gets host statuses for UI binding.
    /// </summary>
    public IReadOnlyDictionary<Guid, HostStatus> HostStatuses => _hostStatusService.GetAllStatuses();

    /// <summary>
    /// Event raised when a new session is created (for pane management).
    /// Delegates to SessionViewModel.
    /// </summary>
    public event EventHandler<TerminalSession>? SessionCreated
    {
        add => Session.SessionCreated += value;
        remove => Session.SessionCreated -= value;
    }

    public MainWindowViewModel(
        HostManagementViewModel hostManagement,
        SessionViewModel session,
        SessionLoggingViewModel sessionLogging,
        BroadcastInputViewModel broadcastInput,
        SftpLauncherViewModel sftpLauncher,
        ImportExportViewModel importExport,
        IHostStatusService hostStatusService,
        ITerminalResizeService terminalResizeService,
        PortForwardingManagerViewModel portForwardingManager,
        IConnectionHistoryRepository connectionHistoryRepository,
        ILogger<MainWindowViewModel>? logger = null)
    {
        HostManagement = hostManagement;
        Session = session;
        SessionLogging = sessionLogging;
        BroadcastInput = broadcastInput;
        SftpLauncher = sftpLauncher;
        ImportExport = importExport;
        _hostStatusService = hostStatusService;
        _terminalResizeService = terminalResizeService;
        PortForwardingManager = portForwardingManager;
        _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;

        // Initialize QuickConnectOverlay
        QuickConnectOverlay = new QuickConnectOverlayViewModel(connectionHistoryRepository);

        _logger.LogDebug("MainWindowViewModel initializing");

        // Startup validation: warn if terminal resize is not available
        if (!_terminalResizeService.IsResizeSupported)
        {
            _logger.LogWarning(
                "Terminal resize is not available - terminal windows may not resize properly. " +
                "This may be due to an SSH.NET library version change.");
        }

        // Subscribe to host status changes
        _hostStatusService.StatusChanged += OnHostStatusChanged;

        // Subscribe to QuickConnectOverlay events
        QuickConnectOverlay.HostSelected += OnQuickConnectHostSelected;

        _logger.LogDebug("MainWindowViewModel initialized");
    }

    /// <summary>
    /// Opens the quick connect overlay for Ctrl+K host search.
    /// </summary>
    public void OpenQuickConnect()
    {
        // Update hosts and statuses in the overlay before opening
        QuickConnectOverlay.SetHosts(HostManagement.Hosts);
        QuickConnectOverlay.HostStatuses = HostStatuses;
        QuickConnectOverlay.Open();
    }

    /// <summary>
    /// Saves a transient host entry (one that was created without going through the Add Host dialog).
    /// Used for Serial Quick Connect when the user wants to save the connection.
    /// </summary>
    public Task SaveTransientHostAsync(HostEntry host) => HostManagement.SaveTransientHostAsync(host);

    private async void OnQuickConnectHostSelected(object? sender, HostEntry host)
    {
        // Connect to the selected host
        // Wrap in try-catch since async void event handlers can swallow exceptions
        try
        {
            await Session.ConnectCommand.ExecuteAsync(host);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to host {HostId} ({DisplayName}) from quick connect",
                host.Id, host.DisplayName);
        }
    }

    private void OnHostStatusChanged(object? sender, EventArgs e)
    {
        // Notify that HostStatuses has changed (UI will re-query)
        OnPropertyChanged(nameof(HostStatuses));
        
        // Also update QuickConnectOverlay's HostStatuses if it's open
        if (QuickConnectOverlay.IsOpen)
        {
            QuickConnectOverlay.HostStatuses = HostStatuses;
        }
    }

    /// <summary>
    /// Loads hosts and groups from the database.
    /// Delegates to HostManagementViewModel.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public Task LoadDataAsync(CancellationToken cancellationToken = default) => 
        HostManagement.LoadDataAsync(cancellationToken);

    /// <summary>
    /// Refreshes the hosts list based on current search text and group filter.
    /// Delegates to HostManagementViewModel.
    /// </summary>
    public Task RefreshHostsAsync() => HostManagement.RefreshHostsAsync();

    /// <summary>
    /// Gets the total host count for each group from the database (unfiltered).
    /// Delegates to HostManagementViewModel.
    /// </summary>
    public Task<(int totalCount, Dictionary<Guid, int> countsByGroup)> GetTotalHostCountsAsync() =>
        HostManagement.GetTotalHostCountsAsync();

    /// <summary>
    /// Creates a session for a host without raising the SessionCreated event.
    /// Used when the caller wants to manage pane creation manually.
    /// Delegates to SessionViewModel.
    /// </summary>
    public Task<TerminalSession?> CreateSessionForHostAsync(HostEntry host) =>
        Session.CreateSessionForHostAsync(host);

    /// <summary>
    /// Gets the broadcast input service for the terminal control.
    /// Delegates to BroadcastInputViewModel.
    /// </summary>
    public IBroadcastInputService BroadcastService => BroadcastInput.BroadcastService;

    /// <summary>
    /// Exports all hosts and groups to a JSON file.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ExportHostsAsync() => ImportExport.ExportHostsAsync();

    /// <summary>
    /// Imports hosts and groups from a JSON file.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ImportHostsAsync() => ImportExport.ImportHostsAsync();

    /// <summary>
    /// Gets the SSH connection service for external access.
    /// Delegates to SessionViewModel.
    /// </summary>
    public ISshConnectionService SshService => Session.SshService;

    /// <summary>
    /// Gets the serial connection service for external access.
    /// Delegates to SessionViewModel.
    /// </summary>
    public ISerialConnectionService SerialConnectionService => Session.SerialConnectionService;

    /// <summary>
    /// Gets the host fingerprint repository for external access.
    /// Delegates to SessionViewModel.
    /// </summary>
    public IHostFingerprintRepository FingerprintRepository => Session.FingerprintRepository;

    /// <summary>
    /// Creates connection info from a host entry and optional password.
    /// Delegates to SessionViewModel.
    /// </summary>
    public Task<TerminalConnectionInfo> CreateConnectionInfoAsync(HostEntry host, string? password) =>
        Session.CreateConnectionInfoAsync(host, password);

    /// <summary>
    /// Creates serial connection info from a host entry.
    /// </summary>
    /// <param name="host">The host entry configured for serial connection.</param>
    /// <returns>Serial connection info populated from the host's serial settings.</returns>
    public SerialConnectionInfo CreateSerialConnectionInfo(HostEntry host) =>
        SerialConnectionInfo.FromHostEntry(host);

    /// <summary>
    /// Records a connection result in the connection history.
    /// Delegates to SessionViewModel.
    /// </summary>
    public Task RecordConnectionResultAsync(
        HostEntry host,
        bool wasSuccessful,
        string? errorMessage,
        DateTimeOffset? connectedAt = null) =>
        Session.RecordConnectionResultAsync(host, wasSuccessful, errorMessage, connectedAt);

    /// <summary>
    /// Creates a keyboard-interactive authentication callback that shows a dialog for 2FA/TOTP prompts.
    /// Delegates to SessionViewModel.
    /// </summary>
    public KeyboardInteractiveCallback CreateKeyboardInteractiveCallback() =>
        Session.CreateKeyboardInteractiveCallback();

    /// <summary>
    /// Creates a host key verification callback for the specified host.
    /// Delegates to SessionViewModel.
    /// </summary>
    public HostKeyVerificationCallback CreateHostKeyVerificationCallback(Guid hostId) =>
        Session.CreateHostKeyVerificationCallback(hostId);

    /// <summary>
    /// Imports hosts from an SSH config file (~/.ssh/config).
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ImportFromSshConfigAsync() => ImportExport.ImportFromSshConfigAsync();

    /// <summary>
    /// Exports hosts to an SSH config file format.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ExportToSshConfigAsync() => ImportExport.ExportToSshConfigAsync();

    /// <summary>
    /// Imports hosts from PuTTY sessions stored in the Windows registry.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public Task ImportFromPuttyAsync() => ImportExport.ImportFromPuttyAsync();

    /// <summary>
    /// Event raised when hosts have been imported.
    /// Delegates to ImportExportViewModel.
    /// </summary>
    public event EventHandler? HostsImported
    {
        add => ImportExport.HostsImported += value;
        remove => ImportExport.HostsImported -= value;
    }

    /// <summary>
    /// Gets the credential cache for external access (e.g., from terminal controls).
    /// Delegates to SessionViewModel.
    /// </summary>
    public ICredentialCache CredentialCache => Session.CredentialCache;

    /// <summary>
    /// Gets the ProxyJump profile repository for external access.
    /// </summary>
    public IProxyJumpProfileRepository ProxyJumpProfileRepository => HostManagement.ProxyJumpProfileRepository;

    /// <summary>
    /// Gets the port forwarding profile repository for external access.
    /// </summary>
    public IPortForwardingProfileRepository PortForwardingProfileRepository => HostManagement.PortForwardingProfileRepository;

    /// <summary>
    /// Gets the host repository for external access.
    /// </summary>
    public IHostRepository HostRepository => HostManagement.HostRepository;

    public void Dispose()
    {
        _hostStatusService.StatusChanged -= OnHostStatusChanged;
        QuickConnectOverlay.HostSelected -= OnQuickConnectHostSelected;
    }
}
