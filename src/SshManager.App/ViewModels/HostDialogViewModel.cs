using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;

namespace SshManager.App.ViewModels;

public partial class HostDialogViewModel : ObservableObject
{
    private readonly ISecretProtector _secretProtector;
    private readonly IProxyJumpProfileRepository? _proxyJumpRepo;
    private readonly IPortForwardingProfileRepository? _portForwardingRepo;
    private readonly ILogger<HostDialogViewModel> _logger;
    private readonly HostEntry _originalHost;

    // Validation regex patterns
    private static readonly Regex HostnameRegex = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex IpAddressRegex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_\-\.]*$", RegexOptions.Compiled);

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _hostname = "";

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private AuthType _authType = AuthType.SshAgent;

    [ObservableProperty]
    private string _privateKeyPath = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private HostGroup? _selectedGroup;

    [ObservableProperty]
    private ObservableCollection<HostGroup> _availableGroups = [];

    [ObservableProperty]
    private bool _isNewHost;

    [ObservableProperty]
    private string? _validationError;

    // ProxyJump and Port Forwarding
    [ObservableProperty]
    private ProxyJumpProfile? _selectedProxyJumpProfile;

    [ObservableProperty]
    private ObservableCollection<ProxyJumpProfile> _availableProxyJumpProfiles = [];

    [ObservableProperty]
    private int _portForwardingProfileCount;

    /// <summary>
    /// Event raised when the user wants to manage ProxyJump profiles.
    /// </summary>
    public event EventHandler? ManageProxyJumpProfilesRequested;

    /// <summary>
    /// Event raised when the user wants to manage port forwarding for this host.
    /// </summary>
    public event EventHandler? ManagePortForwardingRequested;

    public string Title => IsNewHost ? "Add Host" : "Edit Host";

    public IEnumerable<AuthType> AuthTypes => Enum.GetValues<AuthType>();

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    public HostDialogViewModel(
        ISecretProtector secretProtector,
        HostEntry? host = null,
        IEnumerable<HostGroup>? groups = null,
        IProxyJumpProfileRepository? proxyJumpRepo = null,
        IPortForwardingProfileRepository? portForwardingRepo = null,
        ILogger<HostDialogViewModel>? logger = null)
    {
        _secretProtector = secretProtector;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _logger = logger ?? NullLogger<HostDialogViewModel>.Instance;
        _originalHost = host ?? new HostEntry();
        IsNewHost = host == null;

        // Set available groups
        if (groups != null)
        {
            AvailableGroups = new ObservableCollection<HostGroup>(groups);
        }

        // Copy values from host to view model
        DisplayName = _originalHost.DisplayName;
        Hostname = _originalHost.Hostname;
        Port = _originalHost.Port;
        Username = _originalHost.Username;
        AuthType = _originalHost.AuthType;
        PrivateKeyPath = _originalHost.PrivateKeyPath ?? "";
        Notes = _originalHost.Notes;

        // Find the matching group if exists
        if (_originalHost.GroupId.HasValue && groups != null)
        {
            SelectedGroup = AvailableGroups.FirstOrDefault(g => g.Id == _originalHost.GroupId.Value);
        }

        // Decrypt password if available
        if (!string.IsNullOrEmpty(_originalHost.PasswordProtected))
        {
            Password = _secretProtector.TryUnprotect(_originalHost.PasswordProtected) ?? "";
        }

        // Load port forwarding count if host exists
        PortForwardingProfileCount = _originalHost.PortForwardingProfiles?.Count ?? 0;

        _logger.LogDebug("HostDialogViewModel initialized for {Mode} host", IsNewHost ? "new" : "editing");
    }

    /// <summary>
    /// Loads available ProxyJump profiles asynchronously.
    /// Call this after constructing the ViewModel.
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
            if (_originalHost.ProxyJumpProfileId.HasValue)
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
        if (_portForwardingRepo == null || IsNewHost) return;

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

    [RelayCommand]
    private void Save()
    {
        ValidationError = null;

        // Validate and get any errors
        var errors = ValidateHost();
        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            _logger.LogWarning("Host validation failed: {Errors}", string.Join("; ", errors));
            return;
        }

        // Use hostname as display name if not provided
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = Hostname;
        }

        _logger.LogInformation("Host validation passed, saving {DisplayName} ({Hostname}:{Port})", DisplayName, Hostname, Port);
        DialogResult = true;
        RequestClose?.Invoke();
    }

    private List<string> ValidateHost()
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
        else if (!UsernameRegex.IsMatch(username))
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

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

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

    public HostEntry GetHost()
    {
        _originalHost.DisplayName = DisplayName.Trim();
        _originalHost.Hostname = Hostname.Trim();
        _originalHost.Port = Port;
        _originalHost.Username = Username.Trim();
        _originalHost.AuthType = AuthType;
        _originalHost.PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim();
        _originalHost.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        _originalHost.GroupId = SelectedGroup?.Id;
        _originalHost.Group = SelectedGroup;
        _originalHost.ProxyJumpProfileId = SelectedProxyJumpProfile?.Id;
        _originalHost.ProxyJumpProfile = SelectedProxyJumpProfile;
        _originalHost.UpdatedAt = DateTimeOffset.UtcNow;

        // Encrypt password if changed and auth type is password
        if (AuthType == AuthType.Password && !string.IsNullOrEmpty(Password))
        {
            _originalHost.PasswordProtected = _secretProtector.Protect(Password);
        }
        else if (AuthType != AuthType.Password)
        {
            _originalHost.PasswordProtected = null; // Clear password if auth type changed
        }

        return _originalHost;
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
    /// Clears the selected ProxyJump profile.
    /// </summary>
    [RelayCommand]
    private void ClearProxyJumpProfile()
    {
        SelectedProxyJumpProfile = null;
        _logger.LogDebug("Cleared ProxyJump profile selection");
    }

    /// <summary>
    /// Gets the display text for port forwarding status.
    /// </summary>
    public string PortForwardingStatusText => PortForwardingProfileCount switch
    {
        0 => "No port forwards configured",
        1 => "1 port forward configured",
        _ => $"{PortForwardingProfileCount} port forwards configured"
    };

    partial void OnAuthTypeChanged(AuthType value)
    {
        OnPropertyChanged(nameof(ShowPrivateKeyPath));
        OnPropertyChanged(nameof(ShowPassword));
    }

    partial void OnPortForwardingProfileCountChanged(int value)
    {
        OnPropertyChanged(nameof(PortForwardingStatusText));
    }

    public bool ShowPrivateKeyPath => AuthType == AuthType.PrivateKeyFile;
    public bool ShowPassword => AuthType == AuthType.Password;
}
