using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

public partial class HostDialogViewModel : ObservableObject
{
    private readonly ISecretProtector _secretProtector;
    private readonly ISerialConnectionService _serialConnectionService;
    private readonly IAgentDiagnosticsService? _agentDiagnosticsService;
    private readonly IHostProfileRepository? _hostProfileRepo;
    private readonly IProxyJumpProfileRepository? _proxyJumpRepo;
    private readonly IPortForwardingProfileRepository? _portForwardingRepo;
    private readonly ITagRepository? _tagRepo;
    private readonly IHostEnvironmentVariableRepository? _envVarRepo;
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
    private ShellType _shellType = ShellType.Auto;

    [ObservableProperty]
    private string _privateKeyPath = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private string _secureNotes = string.Empty;

    [ObservableProperty]
    private bool _showSecureNotes;

    /// <summary>
    /// Gets or sets the displayed secure notes (masked when hidden, actual content when shown).
    /// </summary>
    public string DisplayedSecureNotes
    {
        get => ShowSecureNotes ? SecureNotes : (string.IsNullOrEmpty(SecureNotes) ? string.Empty : new string('•', Math.Min(SecureNotes.Length, 20)));
        set
        {
            if (ShowSecureNotes)
            {
                SecureNotes = value;
                OnPropertyChanged();
            }
        }
    }

    partial void OnShowSecureNotesChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedSecureNotes));
    }

    partial void OnSecureNotesChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayedSecureNotes));
    }

    // Serial Port Connection Properties
    [ObservableProperty]
    private bool _isSshConnection = true;

    [ObservableProperty]
    private bool _isSerialConnection = false;

    [ObservableProperty]
    private string[] _availablePorts = [];

    [ObservableProperty]
    private string? _serialPortName;

    [ObservableProperty]
    private int _serialBaudRate = 9600;

    [ObservableProperty]
    private int _serialDataBits = 8;

    [ObservableProperty]
    private StopBits _serialStopBits = StopBits.One;

    [ObservableProperty]
    private Parity _serialParity = Parity.None;

    [ObservableProperty]
    private Handshake _serialHandshake = Handshake.None;

    [ObservableProperty]
    private bool _serialDtrEnable = true;

    [ObservableProperty]
    private bool _serialRtsEnable = true;

    [ObservableProperty]
    private bool _serialLocalEcho = false;

    [ObservableProperty]
    private string _serialLineEnding = "\r\n";

    // Keep-Alive Properties (SSH only)
    [ObservableProperty]
    private bool _useGlobalKeepAliveSetting = true;

    [ObservableProperty]
    private int _keepAliveIntervalSeconds = 60;

    // SSH Agent Status Properties
    [ObservableProperty]
    private bool _isAgentAvailable;

    [ObservableProperty]
    private string _agentStatusText = "Checking...";

    [ObservableProperty]
    private bool _isCheckingAgent;

    // Static arrays for ComboBox options
    public static int[] BaudRateOptions { get; } = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400];
    public static int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public static StopBits[] StopBitsOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public static Parity[] ParityOptions { get; } = [Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space];
    public static Handshake[] HandshakeOptions { get; } = [Handshake.None, Handshake.XOnXOff, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff];
    public static string[] LineEndingOptions { get; } = ["\r\n", "\n", "\r"];

    [ObservableProperty]
    private HostGroup? _selectedGroup;

    [ObservableProperty]
    private ObservableCollection<HostGroup> _availableGroups = [];

    [ObservableProperty]
    private HostProfile? _selectedHostProfile;

    [ObservableProperty]
    private ObservableCollection<HostProfile> _availableHostProfiles = [];

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

    [ObservableProperty]
    private ObservableCollection<Tag> _allTags = [];

    [ObservableProperty]
    private ObservableCollection<Tag> _selectedTags = [];

    [ObservableProperty]
    private string _newTagName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoEnvironmentVariables))]
    private ObservableCollection<HostEnvironmentVariableViewModel> _environmentVariables = [];

    /// <summary>
    /// Returns true if there are no environment variables configured.
    /// </summary>
    public bool HasNoEnvironmentVariables => EnvironmentVariables.Count == 0;

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

    public IEnumerable<ShellType> ShellTypes => Enum.GetValues<ShellType>();

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    public HostDialogViewModel(
        ISecretProtector secretProtector,
        ISerialConnectionService serialConnectionService,
        HostEntry? host = null,
        IEnumerable<HostGroup>? groups = null,
        IHostProfileRepository? hostProfileRepo = null,
        IProxyJumpProfileRepository? proxyJumpRepo = null,
        IPortForwardingProfileRepository? portForwardingRepo = null,
        ITagRepository? tagRepo = null,
        IHostEnvironmentVariableRepository? envVarRepo = null,
        IAgentDiagnosticsService? agentDiagnosticsService = null,
        ILogger<HostDialogViewModel>? logger = null)
    {
        _secretProtector = secretProtector;
        _serialConnectionService = serialConnectionService;
        _agentDiagnosticsService = agentDiagnosticsService;
        _hostProfileRepo = hostProfileRepo;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _tagRepo = tagRepo;
        _envVarRepo = envVarRepo;
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
        ShellType = _originalHost.ShellType;
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

        // Decrypt secure notes if available
        if (!string.IsNullOrEmpty(_originalHost.SecureNotesProtected))
        {
            SecureNotes = _secretProtector.TryUnprotect(_originalHost.SecureNotesProtected) ?? "";
        }

        // Load port forwarding count if host exists
        PortForwardingProfileCount = _originalHost.PortForwardingProfiles?.Count ?? 0;

        // Load serial port settings from host
        IsSshConnection = _originalHost.ConnectionType == ConnectionType.Ssh;
        IsSerialConnection = _originalHost.ConnectionType == ConnectionType.Serial;
        SerialPortName = _originalHost.SerialPortName;
        SerialBaudRate = _originalHost.SerialBaudRate;
        SerialDataBits = _originalHost.SerialDataBits;
        SerialStopBits = _originalHost.SerialStopBits;
        SerialParity = _originalHost.SerialParity;
        SerialHandshake = _originalHost.SerialHandshake;
        SerialDtrEnable = _originalHost.SerialDtrEnable;
        SerialRtsEnable = _originalHost.SerialRtsEnable;
        SerialLocalEcho = _originalHost.SerialLocalEcho;
        SerialLineEnding = _originalHost.SerialLineEnding;

        // Load keep-alive settings
        if (_originalHost.KeepAliveIntervalSeconds.HasValue)
        {
            UseGlobalKeepAliveSetting = false;
            KeepAliveIntervalSeconds = _originalHost.KeepAliveIntervalSeconds.Value;
        }
        else
        {
            UseGlobalKeepAliveSetting = true;
            KeepAliveIntervalSeconds = 60; // Default value
        }

        // Initialize available ports list
        RefreshPorts();

        _logger.LogDebug("HostDialogViewModel initialized for {Mode} host", IsNewHost ? "new" : "editing");
    }

    /// <summary>
    /// Loads available host profiles asynchronously.
    /// Call this after constructing the ViewModel.
    /// </summary>
    public async Task LoadHostProfilesAsync(CancellationToken ct = default)
    {
        if (_hostProfileRepo == null) return;

        try
        {
            var profiles = await _hostProfileRepo.GetAllAsync(ct);
            AvailableHostProfiles = new ObservableCollection<HostProfile>(profiles);

            // Set selected profile from host
            if (_originalHost.HostProfileId.HasValue)
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

    /// <summary>
    /// Loads available tags asynchronously.
    /// Call this after constructing the ViewModel.
    /// </summary>
    public async Task LoadTagsAsync(CancellationToken ct = default)
    {
        if (_tagRepo == null) return;

        try
        {
            var tags = await _tagRepo.GetAllAsync(ct);
            AllTags = new ObservableCollection<Tag>(tags);

            // Set selected tags from host
            if (_originalHost.Tags != null && _originalHost.Tags.Any())
            {
                SelectedTags = new ObservableCollection<Tag>(_originalHost.Tags);
            }

            _logger.LogDebug("Loaded {TagCount} available tags, {SelectedCount} selected", tags.Count, SelectedTags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tags");
        }
    }

    /// <summary>
    /// Initializes SSH agent status check if AuthType is SshAgent.
    /// Call this after constructing the ViewModel.
    /// </summary>
    public async Task InitializeAgentStatusAsync(CancellationToken ct = default)
    {
        if (AuthType == AuthType.SshAgent && IsSshConnection)
        {
            await RefreshAgentStatusAsync();
        }
    }

    /// <summary>
    /// Loads environment variables for the host asynchronously.
    /// Call this after constructing the ViewModel.
    /// </summary>
    public async Task LoadEnvironmentVariablesAsync(CancellationToken ct = default)
    {
        if (_envVarRepo == null || IsNewHost) return;

        try
        {
            var envVars = await _envVarRepo.GetByHostIdAsync(_originalHost.Id, ct);
            EnvironmentVariables = new ObservableCollection<HostEnvironmentVariableViewModel>(
                envVars.Select(e => new HostEnvironmentVariableViewModel
                {
                    Name = e.Name,
                    Value = e.Value,
                    IsEnabled = e.IsEnabled
                }));
            OnPropertyChanged(nameof(HasNoEnvironmentVariables));

            _logger.LogDebug("Loaded {EnvVarCount} environment variables for host", envVars.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load environment variables");
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

        // Use hostname/port name as display name if not provided
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = IsSerialConnection ? SerialPortName ?? "Serial" : Hostname;
        }

        if (IsSerialConnection)
        {
            _logger.LogInformation("Host validation passed, saving serial connection {DisplayName} ({SerialPortName})", DisplayName, SerialPortName);
        }
        else
        {
            _logger.LogInformation("Host validation passed, saving {DisplayName} ({Hostname}:{Port})", DisplayName, Hostname, Port);
        }
        DialogResult = true;
        RequestClose?.Invoke();
    }

    private List<string> ValidateHost()
    {
        var errors = new List<string>();

        // Validate based on connection type
        if (IsSerialConnection)
        {
            // Serial port validation
            if (string.IsNullOrWhiteSpace(SerialPortName))
            {
                errors.Add("COM Port is required");
            }

            // Validate baud rate
            if (SerialBaudRate <= 0)
            {
                errors.Add("Baud rate must be a positive number");
            }

            // Validate data bits
            if (SerialDataBits < 5 || SerialDataBits > 8)
            {
                errors.Add("Data bits must be between 5 and 8");
            }
        }
        else
        {
            // SSH connection validation
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
        _originalHost.ShellType = ShellType;
        _originalHost.PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim();
        _originalHost.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        _originalHost.GroupId = SelectedGroup?.Id;
        _originalHost.Group = SelectedGroup;
        _originalHost.HostProfileId = SelectedHostProfile?.Id;
        _originalHost.HostProfile = SelectedHostProfile;
        _originalHost.ProxyJumpProfileId = SelectedProxyJumpProfile?.Id;
        _originalHost.ProxyJumpProfile = SelectedProxyJumpProfile;
        _originalHost.UpdatedAt = DateTimeOffset.UtcNow;

        // Update tags
        _originalHost.Tags = SelectedTags.ToList();

        // Encrypt password if changed and auth type is password
        if (AuthType == AuthType.Password && !string.IsNullOrEmpty(Password))
        {
            _originalHost.PasswordProtected = _secretProtector.Protect(Password);
        }
        else if (AuthType != AuthType.Password)
        {
            _originalHost.PasswordProtected = null; // Clear password if auth type changed
        }

        // Encrypt secure notes if provided
        if (!string.IsNullOrEmpty(SecureNotes))
        {
            _originalHost.SecureNotesProtected = _secretProtector.Protect(SecureNotes);
        }
        else
        {
            _originalHost.SecureNotesProtected = null; // Clear if empty
        }

        // Save serial port settings
        _originalHost.ConnectionType = IsSerialConnection ? ConnectionType.Serial : ConnectionType.Ssh;
        _originalHost.SerialPortName = SerialPortName;
        _originalHost.SerialBaudRate = SerialBaudRate;
        _originalHost.SerialDataBits = SerialDataBits;
        _originalHost.SerialStopBits = SerialStopBits;
        _originalHost.SerialParity = SerialParity;
        _originalHost.SerialHandshake = SerialHandshake;
        _originalHost.SerialDtrEnable = SerialDtrEnable;
        _originalHost.SerialRtsEnable = SerialRtsEnable;
        _originalHost.SerialLocalEcho = SerialLocalEcho;
        _originalHost.SerialLineEnding = SerialLineEnding;

        // Save keep-alive settings
        if (UseGlobalKeepAliveSetting)
        {
            _originalHost.KeepAliveIntervalSeconds = null; // Use global setting
        }
        else
        {
            _originalHost.KeepAliveIntervalSeconds = KeepAliveIntervalSeconds;
        }

        return _originalHost;
    }

    /// <summary>
    /// Gets the environment variables as domain models for saving.
    /// The caller should use IHostEnvironmentVariableRepository.SetForHostAsync() to save these.
    /// </summary>
    public IEnumerable<HostEnvironmentVariable> GetEnvironmentVariables()
    {
        int sortOrder = 0;
        return EnvironmentVariables
            .Where(e => !string.IsNullOrWhiteSpace(e.Name)) // Skip entries with empty names
            .Select(e => new HostEnvironmentVariable
            {
                Id = Guid.NewGuid(),
                HostEntryId = _originalHost.Id,
                Name = e.Name.Trim(),
                Value = e.Value?.Trim() ?? string.Empty,
                IsEnabled = e.IsEnabled,
                SortOrder = sortOrder++
            });
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
    /// Toggles the visibility of secure notes (show/hide sensitive content).
    /// </summary>
    [RelayCommand]
    private void ToggleSecureNotesVisibility()
    {
        ShowSecureNotes = !ShowSecureNotes;
    }

    /// <summary>
    /// Creates a new tag using the NewTagName property.
    /// </summary>
    [RelayCommand]
    private async Task CreateTagAsync()
    {
        if (_tagRepo == null || string.IsNullOrWhiteSpace(NewTagName)) return;

        try
        {
            var tag = await _tagRepo.GetOrCreateAsync(NewTagName.Trim());

            // Add to all tags if not already present
            if (!AllTags.Any(t => t.Id == tag.Id))
            {
                AllTags.Add(tag);
            }

            // Add to selected tags if not already selected
            if (!SelectedTags.Any(t => t.Id == tag.Id))
            {
                SelectedTags.Add(tag);
            }

            _logger.LogInformation("Created/added tag: {TagName}", tag.Name);
            NewTagName = ""; // Clear the input field
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create tag: {TagName}", NewTagName);
        }
    }

    /// <summary>
    /// Toggles a tag's selection state for this host.
    /// </summary>
    [RelayCommand]
    private void ToggleTag(Tag tag)
    {
        var existingTag = SelectedTags.FirstOrDefault(t => t.Id == tag.Id);
        if (existingTag != null)
        {
            SelectedTags.Remove(existingTag);
            _logger.LogDebug("Removed tag: {TagName}", tag.Name);
        }
        else
        {
            SelectedTags.Add(tag);
            _logger.LogDebug("Added tag: {TagName}", tag.Name);
        }
    }

    /// <summary>
    /// Adds a new empty environment variable to the collection.
    /// </summary>
    [RelayCommand]
    private void AddEnvironmentVariable()
    {
        var envVar = new HostEnvironmentVariableViewModel
        {
            Name = string.Empty,
            Value = string.Empty,
            IsEnabled = true
        };
        EnvironmentVariables.Add(envVar);
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Added new environment variable");
    }

    /// <summary>
    /// Removes an environment variable from the collection.
    /// </summary>
    [RelayCommand]
    private void RemoveEnvironmentVariable(HostEnvironmentVariableViewModel envVar)
    {
        if (envVar == null) return;

        EnvironmentVariables.Remove(envVar);
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Removed environment variable: {EnvVarName}", envVar.Name);
    }

    /// <summary>
    /// Adds a preset environment variable from a "NAME=value" format string.
    /// </summary>
    [RelayCommand]
    private void AddPresetEnvironmentVariable(string preset)
    {
        if (string.IsNullOrEmpty(preset)) return;

        var parts = preset.Split('=', 2);
        if (parts.Length != 2) return;

        var name = parts[0].Trim();
        var value = parts[1].Trim();

        // Check if this variable already exists
        if (EnvironmentVariables.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Environment variable {EnvVarName} already exists, skipping", name);
            return;
        }

        var envVar = new HostEnvironmentVariableViewModel
        {
            Name = name,
            Value = value,
            IsEnabled = true
        };
        EnvironmentVariables.Add(envVar);
        OnPropertyChanged(nameof(HasNoEnvironmentVariables));
        _logger.LogDebug("Added preset environment variable: {EnvVarName}={EnvVarValue}", name, value);
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
        OnPropertyChanged(nameof(ShowAgentStatus));

        // Refresh agent status when switching to SSH Agent auth
        if (value == AuthType.SshAgent && IsSshConnection)
        {
            _ = RefreshAgentStatusAsync();
        }
    }

    partial void OnPortForwardingProfileCountChanged(int value)
    {
        OnPropertyChanged(nameof(PortForwardingStatusText));
    }

    /// <summary>
    /// When IsSshConnection changes, toggle IsSerialConnection to act like radio buttons.
    /// </summary>
    partial void OnIsSshConnectionChanged(bool value)
    {
        if (value && IsSerialConnection)
        {
            IsSerialConnection = false;
        }
        else if (!value && !IsSerialConnection)
        {
            IsSerialConnection = true;
        }
    }

    /// <summary>
    /// When IsSerialConnection changes, toggle IsSshConnection to act like radio buttons.
    /// </summary>
    partial void OnIsSerialConnectionChanged(bool value)
    {
        if (value && IsSshConnection)
        {
            IsSshConnection = false;
        }
        else if (!value && !IsSshConnection)
        {
            IsSshConnection = true;
        }
    }

    /// <summary>
    /// Refreshes the list of available serial ports.
    /// </summary>
    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts = _serialConnectionService.GetAvailablePorts();
        if (AvailablePorts.Length > 0 && string.IsNullOrEmpty(SerialPortName))
        {
            SerialPortName = AvailablePorts[0];
        }
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

    public bool ShowPrivateKeyPath => AuthType == AuthType.PrivateKeyFile;
    public bool ShowPassword => AuthType == AuthType.Password;
    public bool ShowAgentStatus => AuthType == AuthType.SshAgent && IsSshConnection;
}
