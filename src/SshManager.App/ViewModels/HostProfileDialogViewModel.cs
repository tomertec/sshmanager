using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for adding or editing a single host profile.
/// </summary>
public partial class HostProfileDialogViewModel : ObservableObject
{
    private readonly IProxyJumpProfileRepository _proxyJumpRepository;
    private readonly ILogger<HostProfileManagerViewModel> _logger;
    private readonly HostProfile _originalProfile;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private int _defaultPort = 22;

    [ObservableProperty]
    private string? _defaultUsername;

    [ObservableProperty]
    private AuthType _authType = AuthType.SshAgent;

    [ObservableProperty]
    private string? _privateKeyPath;

    [ObservableProperty]
    private ProxyJumpProfile? _selectedProxyJumpProfile;

    [ObservableProperty]
    private ObservableCollection<ProxyJumpProfile> _availableProxyJumpProfiles = [];

    [ObservableProperty]
    private bool _isNewProfile;

    [ObservableProperty]
    private string? _validationError;

    public string Title => IsNewProfile ? "Add Host Profile" : "Edit Host Profile";

    public IEnumerable<AuthType> AuthTypes => Enum.GetValues<AuthType>();

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    public HostProfileDialogViewModel(
        IProxyJumpProfileRepository proxyJumpRepository,
        ILogger<HostProfileManagerViewModel> logger,
        HostProfile? profile = null)
    {
        _proxyJumpRepository = proxyJumpRepository;
        _logger = logger;
        _originalProfile = profile ?? new HostProfile();
        IsNewProfile = profile == null;

        // Copy values from profile to view model
        DisplayName = _originalProfile.DisplayName;
        Description = _originalProfile.Description;
        DefaultPort = _originalProfile.DefaultPort;
        DefaultUsername = _originalProfile.DefaultUsername;
        AuthType = _originalProfile.AuthType;
        PrivateKeyPath = _originalProfile.PrivateKeyPath;

        _logger.LogDebug("HostProfileDialogViewModel initialized for {Mode} profile", IsNewProfile ? "new" : "editing");
    }

    /// <summary>
    /// Loads available ProxyJump profiles asynchronously.
    /// </summary>
    public async Task LoadProxyJumpProfilesAsync(CancellationToken ct = default)
    {
        try
        {
            var profiles = await _proxyJumpRepository.GetAllAsync(ct);
            AvailableProxyJumpProfiles = new ObservableCollection<ProxyJumpProfile>(
                profiles.Where(p => p.IsEnabled));

            // Set selected profile from original profile
            if (_originalProfile.ProxyJumpProfileId.HasValue)
            {
                SelectedProxyJumpProfile = AvailableProxyJumpProfiles
                    .FirstOrDefault(p => p.Id == _originalProfile.ProxyJumpProfileId.Value);
            }

            _logger.LogDebug("Loaded {ProfileCount} available ProxyJump profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ProxyJump profiles");
        }
    }

    [RelayCommand]
    private void Save()
    {
        ValidationError = null;

        // Validate
        var errors = ValidateProfile();
        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            _logger.LogWarning("Host profile validation failed: {Errors}", string.Join("; ", errors));
            return;
        }

        _logger.LogInformation("Host profile validation passed, saving {DisplayName}", DisplayName);
        DialogResult = true;
        RequestClose?.Invoke();
    }

    private List<string> ValidateProfile()
    {
        var errors = new List<string>();

        // Display name validation
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            errors.Add("Display name is required");
        }

        // Port validation
        if (DefaultPort < 1 || DefaultPort > 65535)
        {
            errors.Add("Port must be between 1 and 65535");
        }

        // Auth-type specific validation
        if (AuthType == AuthType.PrivateKeyFile)
        {
            var keyPath = PrivateKeyPath?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                errors.Add("Private key file path is required for PrivateKeyFile authentication");
            }
            else if (!System.IO.File.Exists(keyPath))
            {
                errors.Add($"Private key file not found: {keyPath}");
            }
        }

        return errors;
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

    [RelayCommand]
    private void ClearProxyJumpProfile()
    {
        SelectedProxyJumpProfile = null;
        _logger.LogDebug("Cleared ProxyJump profile selection");
    }

    public HostProfile GetProfile()
    {
        _originalProfile.DisplayName = DisplayName.Trim();
        _originalProfile.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        _originalProfile.DefaultPort = DefaultPort;
        _originalProfile.DefaultUsername = string.IsNullOrWhiteSpace(DefaultUsername) ? null : DefaultUsername.Trim();
        _originalProfile.AuthType = AuthType;
        _originalProfile.PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim();
        _originalProfile.ProxyJumpProfileId = SelectedProxyJumpProfile?.Id;
        _originalProfile.ProxyJumpProfile = SelectedProxyJumpProfile;

        return _originalProfile;
    }

    partial void OnAuthTypeChanged(AuthType value)
    {
        OnPropertyChanged(nameof(ShowPrivateKeyPath));
    }

    public bool ShowPrivateKeyPath => AuthType == AuthType.PrivateKeyFile;
}
