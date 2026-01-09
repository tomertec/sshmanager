using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for creating or editing a port forwarding profile.
/// </summary>
public partial class PortForwardingProfileDialogViewModel : ObservableObject
{
    private readonly IPortForwardingProfileRepository _repository;
    private readonly ILogger<PortForwardingProfileDialogViewModel> _logger;
    private readonly PortForwardingProfile? _existingProfile;
    private readonly Guid? _defaultHostId;

    // Validation patterns
    private static readonly Regex BindAddressRegex = new(@"^(?:\d{1,3}\.){3}\d{1,3}$|^localhost$|^\*$|^::1?$|^0\.0\.0\.0$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HostnameRegex = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteFields))]
    [NotifyPropertyChangedFor(nameof(ForwardingTypeDescription))]
    private PortForwardingType _forwardingType = PortForwardingType.LocalForward;

    [ObservableProperty]
    private string _localBindAddress = "127.0.0.1";

    [ObservableProperty]
    private int _localPort = 8080;

    [ObservableProperty]
    private string _remoteHost = "localhost";

    [ObservableProperty]
    private int _remotePort = 80;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private HostEntry? _selectedHost;

    [ObservableProperty]
    private ObservableCollection<HostEntry> _availableHosts = [];

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Whether this is a new profile or editing an existing one.
    /// </summary>
    public bool IsNewProfile => _existingProfile == null;

    /// <summary>
    /// Dialog title based on create/edit mode.
    /// </summary>
    public string Title => IsNewProfile ? "Create Port Forwarding" : "Edit Port Forwarding";

    /// <summary>
    /// Available port forwarding types for binding.
    /// </summary>
    public IReadOnlyList<PortForwardingType> ForwardingTypes { get; } =
        Enum.GetValues<PortForwardingType>().ToList();

    /// <summary>
    /// Whether to show remote host/port fields (hidden for Dynamic forwarding).
    /// </summary>
    public bool ShowRemoteFields => ForwardingType != PortForwardingType.DynamicForward;

    /// <summary>
    /// Description of the current forwarding type.
    /// </summary>
    public string ForwardingTypeDescription => ForwardingType switch
    {
        PortForwardingType.LocalForward =>
            "Local Forward (-L): Forwards traffic from a local port to a remote destination through the SSH tunnel.",
        PortForwardingType.RemoteForward =>
            "Remote Forward (-R): Forwards traffic from a remote port on the server back to a local destination.",
        PortForwardingType.DynamicForward =>
            "Dynamic Forward (-D): Creates a SOCKS5 proxy on the local port for tunneling all traffic.",
        _ => string.Empty
    };

    /// <summary>
    /// Preview of the SSH command equivalent.
    /// </summary>
    public string CommandPreview
    {
        get
        {
            return ForwardingType switch
            {
                PortForwardingType.LocalForward =>
                    $"-L {LocalBindAddress}:{LocalPort}:{RemoteHost}:{RemotePort}",
                PortForwardingType.RemoteForward =>
                    $"-R {LocalBindAddress}:{LocalPort}:{RemoteHost}:{RemotePort}",
                PortForwardingType.DynamicForward =>
                    $"-D {LocalBindAddress}:{LocalPort}",
                _ => string.Empty
            };
        }
    }

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public PortForwardingProfileDialogViewModel(
        IPortForwardingProfileRepository repository,
        PortForwardingProfile? existingProfile = null,
        Guid? defaultHostId = null,
        ILogger<PortForwardingProfileDialogViewModel>? logger = null)
    {
        _repository = repository;
        _existingProfile = existingProfile;
        _defaultHostId = defaultHostId;
        _logger = logger ?? NullLogger<PortForwardingProfileDialogViewModel>.Instance;

        if (existingProfile != null)
        {
            DisplayName = existingProfile.DisplayName;
            Description = existingProfile.Description;
            ForwardingType = existingProfile.ForwardingType;
            LocalBindAddress = existingProfile.LocalBindAddress;
            LocalPort = existingProfile.LocalPort;
            RemoteHost = existingProfile.RemoteHost ?? "localhost";
            RemotePort = existingProfile.RemotePort ?? 80;
            IsEnabled = existingProfile.IsEnabled;
            AutoStart = existingProfile.AutoStart;
        }

        _logger.LogDebug("PortForwardingProfileDialogViewModel initialized for {Mode} profile",
            IsNewProfile ? "new" : "editing");
    }

    /// <summary>
    /// Loads available hosts for the host association selector.
    /// </summary>
    public void LoadAvailableHosts(IEnumerable<HostEntry> hosts)
    {
        AvailableHosts = new ObservableCollection<HostEntry>(hosts);

        // Set selected host from existing profile or default
        if (_existingProfile?.HostId != null)
        {
            SelectedHost = AvailableHosts.FirstOrDefault(h => h.Id == _existingProfile.HostId);
        }
        else if (_defaultHostId != null)
        {
            SelectedHost = AvailableHosts.FirstOrDefault(h => h.Id == _defaultHostId);
        }

        _logger.LogDebug("Loaded {HostCount} available hosts", AvailableHosts.Count);
    }

    partial void OnForwardingTypeChanged(PortForwardingType value)
    {
        OnPropertyChanged(nameof(CommandPreview));

        // Set sensible defaults when switching types
        if (value == PortForwardingType.DynamicForward)
        {
            LocalPort = 1080; // Standard SOCKS port
        }
    }

    partial void OnLocalBindAddressChanged(string value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnLocalPortChanged(int value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnRemoteHostChanged(string value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnRemotePortChanged(int value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationError = null;

        // Validate
        var errors = await ValidateProfileAsync();
        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            _logger.LogWarning("Profile validation failed: {Errors}", string.Join("; ", errors));
            return;
        }

        try
        {
            IsLoading = true;

            if (_existingProfile != null)
            {
                // Update existing profile
                _existingProfile.DisplayName = DisplayName.Trim();
                _existingProfile.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
                _existingProfile.ForwardingType = ForwardingType;
                _existingProfile.LocalBindAddress = LocalBindAddress.Trim();
                _existingProfile.LocalPort = LocalPort;
                _existingProfile.RemoteHost = ForwardingType == PortForwardingType.DynamicForward
                    ? null
                    : RemoteHost.Trim();
                _existingProfile.RemotePort = ForwardingType == PortForwardingType.DynamicForward
                    ? null
                    : RemotePort;
                _existingProfile.IsEnabled = IsEnabled;
                _existingProfile.AutoStart = AutoStart;
                _existingProfile.HostId = SelectedHost?.Id;
                _existingProfile.UpdatedAt = DateTimeOffset.UtcNow;

                await _repository.UpdateAsync(_existingProfile);
                _logger.LogInformation("Updated port forwarding profile: {DisplayName}", DisplayName);
            }
            else
            {
                // Create new profile
                var profile = new PortForwardingProfile
                {
                    DisplayName = DisplayName.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    ForwardingType = ForwardingType,
                    LocalBindAddress = LocalBindAddress.Trim(),
                    LocalPort = LocalPort,
                    RemoteHost = ForwardingType == PortForwardingType.DynamicForward
                        ? null
                        : RemoteHost.Trim(),
                    RemotePort = ForwardingType == PortForwardingType.DynamicForward
                        ? null
                        : RemotePort,
                    IsEnabled = IsEnabled,
                    AutoStart = AutoStart,
                    HostId = SelectedHost?.Id
                };

                await _repository.AddAsync(profile);
                _logger.LogInformation("Created port forwarding profile: {DisplayName}", DisplayName);
            }

            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save port forwarding profile");
            ValidationError = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    private async Task<List<string>> ValidateProfileAsync()
    {
        var errors = new List<string>();

        // Display name validation
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add("Display name is required.");
        }
        else if (DisplayName.Length > 200)
        {
            errors.Add("Display name cannot exceed 200 characters.");
        }

        if (Description?.Length > 1000)
        {
            errors.Add("Description cannot exceed 1000 characters.");
        }

        // Local bind address validation
        var bindAddress = LocalBindAddress?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            errors.Add("Local bind address is required.");
        }
        else if (!BindAddressRegex.IsMatch(bindAddress))
        {
            errors.Add("Invalid local bind address format. Use IP address, localhost, *, or ::1.");
        }

        // Local port validation
        if (LocalPort < 1 || LocalPort > 65535)
        {
            errors.Add("Local port must be between 1 and 65535.");
        }

        // Check if port is already in use
        var excludeId = _existingProfile?.Id;
        if (await _repository.IsPortInUseAsync(LocalPort, excludeId))
        {
            errors.Add($"Local port {LocalPort} is already used by another forwarding profile.");
        }

        // Remote host/port validation (only for Local and Remote forwarding)
        if (ForwardingType != PortForwardingType.DynamicForward)
        {
            var remoteHost = RemoteHost?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(remoteHost))
            {
                errors.Add("Remote host is required for this forwarding type.");
            }
            else if (!HostnameRegex.IsMatch(remoteHost) && !IsValidIpAddress(remoteHost))
            {
                errors.Add("Invalid remote host format.");
            }

            if (RemotePort < 1 || RemotePort > 65535)
            {
                errors.Add("Remote port must be between 1 and 65535.");
            }
        }

        return errors;
    }

    private static bool IsValidIpAddress(string address)
    {
        return System.Net.IPAddress.TryParse(address, out _);
    }

    /// <summary>
    /// Gets the resulting profile after successful save.
    /// </summary>
    public PortForwardingProfile? GetProfile()
    {
        return _existingProfile;
    }
}
