using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels.HostEdit;

/// <summary>
/// ViewModel for SSH connection settings in the host edit dialog.
/// Contains all SSH connection configuration properties including authentication,
/// host profiles, proxy jump, port forwarding, keep-alive, X11 forwarding,
/// SSH agent status, and Kerberos settings.
/// </summary>
public partial class SshConnectionSettingsViewModel : ObservableObject
{
    private readonly ISecretProtector _secretProtector;
    private readonly IAgentDiagnosticsService? _agentDiagnosticsService;
    private readonly IKerberosAuthService? _kerberosAuthService;
    private readonly IHostProfileRepository? _hostProfileRepo;
    private readonly IProxyJumpProfileRepository? _proxyJumpRepo;
    private readonly IPortForwardingProfileRepository? _portForwardingRepo;
    private readonly ILogger<SshConnectionSettingsViewModel> _logger;

    // Store original host for loading profiles by ID
    private HostEntry? _originalHost;

    #region Basic Connection Properties

    [ObservableProperty]
    private string _hostname = "";

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private AuthType _authType = AuthType.SshAgent;

    [ObservableProperty]
    private ShellType _shellType = ShellType.Auto;

    [ObservableProperty]
    private string _privateKeyPath = "";

    [ObservableProperty]
    private string _password = "";

    #endregion

    #region Host/Proxy Profiles Properties

    [ObservableProperty]
    private HostProfile? _selectedHostProfile;

    [ObservableProperty]
    private ObservableCollection<HostProfile> _availableHostProfiles = [];

    [ObservableProperty]
    private ProxyJumpProfile? _selectedProxyJumpProfile;

    [ObservableProperty]
    private ObservableCollection<ProxyJumpProfile> _availableProxyJumpProfiles = [];

    [ObservableProperty]
    private int _portForwardingProfileCount;

    #endregion

    #region Keep-Alive Properties

    [ObservableProperty]
    private bool _useGlobalKeepAliveSetting = true;

    [ObservableProperty]
    private int _keepAliveIntervalSeconds = 60;

    #endregion

    #region X11 Forwarding Properties

    [ObservableProperty]
    private bool? _x11ForwardingEnabled;

    [ObservableProperty]
    private bool _x11TrustedForwarding;

    [ObservableProperty]
    private int? _x11DisplayNumber;

    #endregion

    #region SSH Agent Status Properties

    [ObservableProperty]
    private bool _isAgentAvailable;

    [ObservableProperty]
    private string _agentStatusText = "Checking...";

    [ObservableProperty]
    private bool _isCheckingAgent;

    #endregion

    #region Kerberos Properties

    [ObservableProperty]
    private string? _kerberosServicePrincipal;

    [ObservableProperty]
    private bool _kerberosDelegateCredentials;

    [ObservableProperty]
    private bool _isKerberosAvailable;

    [ObservableProperty]
    private string _kerberosStatusText = "Checking...";

    [ObservableProperty]
    private bool _isCheckingKerberos;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether to show the private key path field.
    /// </summary>
    public bool ShowPrivateKeyPath => AuthType == AuthType.PrivateKeyFile;

    /// <summary>
    /// Gets whether to show the password field.
    /// </summary>
    public bool ShowPassword => AuthType == AuthType.Password;

    /// <summary>
    /// Gets whether to show the SSH agent status section.
    /// </summary>
    public bool ShowAgentStatus => AuthType == AuthType.SshAgent;

    /// <summary>
    /// Gets whether to show Kerberos settings.
    /// </summary>
    public bool ShowKerberosSettings => AuthType == AuthType.Kerberos;

    /// <summary>
    /// Gets the display text for port forwarding status.
    /// </summary>
    public string PortForwardingStatusText => PortForwardingProfileCount switch
    {
        0 => "No port forwards configured",
        1 => "1 port forward configured",
        _ => $"{PortForwardingProfileCount} port forwards configured"
    };

    #endregion

    #region Static Properties

    /// <summary>
    /// Gets the available authentication types.
    /// </summary>
    public static IEnumerable<AuthType> AuthTypes => Enum.GetValues<AuthType>();

    /// <summary>
    /// Gets the available shell types.
    /// </summary>
    public static IEnumerable<ShellType> ShellTypes => Enum.GetValues<ShellType>();

    #endregion

    #region Events

    /// <summary>
    /// Event raised when the user wants to manage ProxyJump profiles.
    /// </summary>
    public event EventHandler? ManageProxyJumpProfilesRequested;

    /// <summary>
    /// Event raised when the user wants to manage port forwarding for this host.
    /// </summary>
    public event EventHandler? ManagePortForwardingRequested;

    #endregion

    /// <summary>
    /// Creates a new instance of the SshConnectionSettingsViewModel.
    /// </summary>
    /// <param name="secretProtector">Service for password encryption/decryption.</param>
    /// <param name="agentDiagnosticsService">Optional SSH agent diagnostics service.</param>
    /// <param name="kerberosAuthService">Optional Kerberos authentication service.</param>
    /// <param name="hostProfileRepo">Optional host profile repository.</param>
    /// <param name="proxyJumpRepo">Optional proxy jump profile repository.</param>
    /// <param name="portForwardingRepo">Optional port forwarding profile repository.</param>
    /// <param name="host">Optional host entry to load settings from.</param>
    /// <param name="logger">Optional logger.</param>
    public SshConnectionSettingsViewModel(
        ISecretProtector secretProtector,
        IAgentDiagnosticsService? agentDiagnosticsService = null,
        IKerberosAuthService? kerberosAuthService = null,
        IHostProfileRepository? hostProfileRepo = null,
        IProxyJumpProfileRepository? proxyJumpRepo = null,
        IPortForwardingProfileRepository? portForwardingRepo = null,
        HostEntry? host = null,
        ILogger<SshConnectionSettingsViewModel>? logger = null)
    {
        _secretProtector = secretProtector;
        _agentDiagnosticsService = agentDiagnosticsService;
        _kerberosAuthService = kerberosAuthService;
        _hostProfileRepo = hostProfileRepo;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _logger = logger ?? NullLogger<SshConnectionSettingsViewModel>.Instance;

        // Load settings from host if provided
        if (host != null)
        {
            LoadFromHost(host);
        }
    }

    #region Load/Populate Methods

    /// <summary>
    /// Loads SSH connection settings from a HostEntry.
    /// </summary>
    /// <param name="host">The host entry to load settings from.</param>
    public void LoadFromHost(HostEntry host)
    {
        _originalHost = host;

        // Basic connection
        Hostname = host.Hostname;
        Port = host.Port;
        Username = host.Username;
        AuthType = host.AuthType;
        ShellType = host.ShellType;
        PrivateKeyPath = host.PrivateKeyPath ?? "";

        // Decrypt password if available
        if (!string.IsNullOrEmpty(host.PasswordProtected))
        {
            Password = _secretProtector.TryUnprotect(host.PasswordProtected) ?? "";
        }
        else
        {
            Password = "";
        }

        // Kerberos settings
        KerberosServicePrincipal = host.KerberosServicePrincipal;
        KerberosDelegateCredentials = host.KerberosDelegateCredentials;

        // Keep-alive settings
        if (host.KeepAliveIntervalSeconds.HasValue)
        {
            UseGlobalKeepAliveSetting = false;
            KeepAliveIntervalSeconds = host.KeepAliveIntervalSeconds.Value;
        }
        else
        {
            UseGlobalKeepAliveSetting = true;
            KeepAliveIntervalSeconds = 60; // Default value
        }

        // X11 forwarding settings
        X11ForwardingEnabled = host.X11ForwardingEnabled;
        X11TrustedForwarding = host.X11TrustedForwarding;
        X11DisplayNumber = host.X11DisplayNumber;

        // Port forwarding count
        PortForwardingProfileCount = host.PortForwardingProfiles?.Count ?? 0;

        _logger.LogDebug("Loaded SSH connection settings from host {HostId}", host.Id);
    }

    /// <summary>
    /// Populates a HostEntry with the current SSH connection settings.
    /// </summary>
    /// <param name="host">The host entry to populate.</param>
    public void PopulateHost(HostEntry host)
    {
        // Basic connection
        host.Hostname = Hostname.Trim();
        host.Port = Port;
        host.Username = Username.Trim();
        host.AuthType = AuthType;
        host.ShellType = ShellType;
        host.PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim();

        // Encrypt password if auth type is password
        if (AuthType == AuthType.Password && !string.IsNullOrEmpty(Password))
        {
            host.PasswordProtected = _secretProtector.Protect(Password);
        }
        else if (AuthType != AuthType.Password)
        {
            host.PasswordProtected = null; // Clear password if auth type changed
        }

        // Kerberos settings
        if (AuthType == AuthType.Kerberos)
        {
            host.KerberosServicePrincipal = string.IsNullOrWhiteSpace(KerberosServicePrincipal)
                ? null
                : KerberosServicePrincipal.Trim();
            host.KerberosDelegateCredentials = KerberosDelegateCredentials;
        }
        else
        {
            host.KerberosServicePrincipal = null;
            host.KerberosDelegateCredentials = false;
        }

        // Host/Proxy profiles
        host.HostProfileId = SelectedHostProfile?.Id;
        host.HostProfile = SelectedHostProfile;
        host.ProxyJumpProfileId = SelectedProxyJumpProfile?.Id;
        host.ProxyJumpProfile = SelectedProxyJumpProfile;

        // Keep-alive settings
        if (UseGlobalKeepAliveSetting)
        {
            host.KeepAliveIntervalSeconds = null; // Use global setting
        }
        else
        {
            host.KeepAliveIntervalSeconds = KeepAliveIntervalSeconds;
        }

        // X11 forwarding settings
        host.X11ForwardingEnabled = X11ForwardingEnabled;
        host.X11TrustedForwarding = X11TrustedForwarding;
        host.X11DisplayNumber = X11DisplayNumber;

        _logger.LogDebug("Populated host with SSH connection settings");
    }

    #endregion

    #region Async Load Methods

    /// <summary>
    /// Loads all async data (profiles, agent status, etc.).
    /// Call this after constructing the ViewModel.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var tasks = new List<Task>
        {
            LoadHostProfilesAsync(ct),
            LoadProxyJumpProfilesAsync(ct),
            LoadPortForwardingCountAsync(ct)
        };

        // Initialize agent status if appropriate
        if (AuthType == AuthType.SshAgent)
        {
            tasks.Add(RefreshAgentStatusAsync());
        }

        // Initialize Kerberos status if appropriate
        if (AuthType == AuthType.Kerberos)
        {
            tasks.Add(RefreshKerberosStatusAsync());
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Loads available host profiles asynchronously.
    /// </summary>
    public async Task LoadHostProfilesAsync(CancellationToken ct = default)
    {
        if (_hostProfileRepo == null) return;

        try
        {
            var profiles = await _hostProfileRepo.GetAllAsync(ct);
            AvailableHostProfiles = new ObservableCollection<HostProfile>(profiles);

            // Set selected profile from host
            if (_originalHost?.HostProfileId.HasValue == true)
            {
                SelectedHostProfile = AvailableHostProfiles
                    .FirstOrDefault(p => p.Id == _originalHost.HostProfileId.Value);
            }

            _logger.LogDebug("Loaded {ProfileCount} available host profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load host profiles");
        }
    }

    /// <summary>
    /// Loads available ProxyJump profiles asynchronously.
    /// </summary>
    public async Task LoadProxyJumpProfilesAsync(CancellationToken ct = default)
    {
        if (_proxyJumpRepo == null) return;

        try
        {
            var profiles = await _proxyJumpRepo.GetAllAsync(ct);
            AvailableProxyJumpProfiles = new ObservableCollection<ProxyJumpProfile>(
                profiles.Where(p => p.IsEnabled));

            // Set selected profile from host
            if (_originalHost?.ProxyJumpProfileId.HasValue == true)
            {
                SelectedProxyJumpProfile = AvailableProxyJumpProfiles
                    .FirstOrDefault(p => p.Id == _originalHost.ProxyJumpProfileId.Value);
            }

            _logger.LogDebug("Loaded {ProfileCount} available ProxyJump profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ProxyJump profiles");
        }
    }

    /// <summary>
    /// Loads the port forwarding profile count for the host.
    /// </summary>
    public async Task LoadPortForwardingCountAsync(CancellationToken ct = default)
    {
        if (_portForwardingRepo == null || _originalHost == null || _originalHost.Id == Guid.Empty) return;

        try
        {
            var profiles = await _portForwardingRepo.GetByHostIdAsync(_originalHost.Id, ct);
            PortForwardingProfileCount = profiles.Count;
            _logger.LogDebug("Found {ProfileCount} port forwarding profiles for host", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load port forwarding profile count");
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Opens a file dialog to browse for a private key file.
    /// </summary>
    [RelayCommand]
    private void BrowsePrivateKey()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Private Key File",
            Filter = "All Files (*.*)|*.*|Private Key Files (*.pem)|*.pem|OpenSSH Keys|id_*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\.ssh"
        };

        if (dialog.ShowDialog() == true)
        {
            PrivateKeyPath = dialog.FileName;
        }
    }

    /// <summary>
    /// Clears the selected host profile.
    /// </summary>
    [RelayCommand]
    private void ClearHostProfile()
    {
        SelectedHostProfile = null;
        _logger.LogDebug("Cleared host profile selection");
    }

    /// <summary>
    /// Clears the selected ProxyJump profile.
    /// </summary>
    [RelayCommand]
    private void ClearProxyJumpProfile()
    {
        SelectedProxyJumpProfile = null;
        _logger.LogDebug("Cleared ProxyJump profile selection");
    }

    /// <summary>
    /// Opens the ProxyJump profiles manager dialog.
    /// </summary>
    [RelayCommand]
    private void ManageProxyJumpProfiles()
    {
        ManageProxyJumpProfilesRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the port forwarding manager for this host.
    /// </summary>
    [RelayCommand]
    private void ManagePortForwarding()
    {
        ManagePortForwardingRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Refreshes SSH agent status information.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAgentStatusAsync()
    {
        if (_agentDiagnosticsService == null)
        {
            AgentStatusText = "Agent diagnostics not available";
            IsAgentAvailable = false;
            return;
        }

        IsCheckingAgent = true;

        try
        {
            await _agentDiagnosticsService.RefreshAsync();
            var result = await _agentDiagnosticsService.GetDiagnosticsAsync();

            IsAgentAvailable = result.PageantAvailable || result.OpenSshAgentAvailable;

            if (result.ActiveAgentType != null && result.Keys.Count > 0)
            {
                var keyText = result.Keys.Count == 1 ? "1 key" : $"{result.Keys.Count} keys";
                AgentStatusText = $"{result.ActiveAgentType}: {keyText} loaded";
            }
            else if (result.PageantAvailable || result.OpenSshAgentAvailable)
            {
                var agentName = result.ActiveAgentType ?? "SSH agent";
                AgentStatusText = $"{agentName}: No keys loaded";
            }
            else
            {
                AgentStatusText = "No SSH agent detected";
            }

            _logger.LogDebug("Agent status refreshed: {StatusText}", AgentStatusText);
        }
        catch (Exception ex)
        {
            AgentStatusText = "Error checking agent status";
            IsAgentAvailable = false;
            _logger.LogWarning(ex, "Failed to refresh SSH agent status");
        }
        finally
        {
            IsCheckingAgent = false;
        }
    }

    /// <summary>
    /// Refreshes Kerberos authentication status information.
    /// </summary>
    [RelayCommand]
    private async Task RefreshKerberosStatusAsync()
    {
        if (_kerberosAuthService == null)
        {
            KerberosStatusText = "Kerberos diagnostics not available";
            IsKerberosAvailable = false;
            return;
        }

        IsCheckingKerberos = true;

        try
        {
            await _kerberosAuthService.RefreshAsync();
            var status = await _kerberosAuthService.GetStatusAsync();

            IsKerberosAvailable = status.IsAvailable && status.HasValidTgt;

            if (status.HasValidTgt)
            {
                var expirationText = status.TgtExpiration.HasValue
                    ? $" (expires {status.TgtExpiration.Value:g})"
                    : "";
                KerberosStatusText = $"{status.Principal}{expirationText}";
            }
            else if (status.IsAvailable)
            {
                KerberosStatusText = status.StatusMessage;
            }
            else
            {
                KerberosStatusText = status.Error ?? "Kerberos not available";
            }

            _logger.LogDebug("Kerberos status refreshed: {StatusText}", KerberosStatusText);
        }
        catch (Exception ex)
        {
            KerberosStatusText = "Error checking Kerberos status";
            IsKerberosAvailable = false;
            _logger.LogWarning(ex, "Failed to refresh Kerberos status");
        }
        finally
        {
            IsCheckingKerberos = false;
        }
    }

    #endregion

    #region Property Changed Handlers

    partial void OnAuthTypeChanged(AuthType value)
    {
        OnPropertyChanged(nameof(ShowPrivateKeyPath));
        OnPropertyChanged(nameof(ShowPassword));
        OnPropertyChanged(nameof(ShowAgentStatus));
        OnPropertyChanged(nameof(ShowKerberosSettings));

        // Refresh agent status when switching to SSH Agent auth
        if (value == AuthType.SshAgent)
        {
            _ = RefreshAgentStatusAsync();
        }

        // Refresh Kerberos status when switching to Kerberos auth
        if (value == AuthType.Kerberos)
        {
            _ = RefreshKerberosStatusAsync();
        }
    }

    partial void OnPortForwardingProfileCountChanged(int value)
    {
        OnPropertyChanged(nameof(PortForwardingStatusText));
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates the SSH connection settings.
    /// </summary>
    /// <returns>A list of validation error messages, empty if valid.</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // Hostname validation
        var hostname = Hostname?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(hostname))
        {
            errors.Add("Hostname is required");
        }
        else if (!IsValidHostname(hostname) && !IsValidIpAddress(hostname))
        {
            errors.Add("Invalid hostname or IP address format");
        }

        // Port validation
        if (Port < 1 || Port > 65535)
        {
            errors.Add("Port must be between 1 and 65535");
        }

        // Username validation
        var username = Username?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(username))
        {
            errors.Add("Username is required");
        }
        else if (username.Length > 32)
        {
            errors.Add("Username must be 32 characters or less");
        }
        else if (!IsValidUsername(username))
        {
            errors.Add("Username contains invalid characters");
        }

        // Auth-type specific validation
        switch (AuthType)
        {
            case AuthType.Password:
                if (string.IsNullOrEmpty(Password))
                {
                    errors.Add("Password is required for password authentication");
                }
                break;

            case AuthType.PrivateKeyFile:
                var keyPath = PrivateKeyPath?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(keyPath))
                {
                    errors.Add("Private key file path is required");
                }
                else if (!File.Exists(keyPath))
                {
                    errors.Add($"Private key file not found: {keyPath}");
                }
                break;
        }

        return errors;
    }

    // Validation regex patterns
    private static readonly System.Text.RegularExpressions.Regex HostnameRegex = 
        new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex IpAddressRegex = 
        new(@"^(\d{1,3}\.){3}\d{1,3}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex UsernameRegex = 
        new(@"^[a-zA-Z_][a-zA-Z0-9_\-\.]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsValidHostname(string hostname)
    {
        if (hostname.Length > 253) return false;
        return HostnameRegex.IsMatch(hostname);
    }

    private static bool IsValidIpAddress(string ip)
    {
        if (!IpAddressRegex.IsMatch(ip)) return false;

        var parts = ip.Split('.');
        return parts.All(p => int.TryParse(p, out var num) && num >= 0 && num <= 255);
    }

    private static bool IsValidUsername(string username)
    {
        return UsernameRegex.IsMatch(username);
    }

    #endregion
}
