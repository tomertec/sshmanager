using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for creating or editing a ProxyJump profile.
/// </summary>
public partial class ProxyJumpProfileDialogViewModel : ObservableObject, IDisposable
{
    private readonly IProxyJumpProfileRepository _repository;
    private readonly IHostRepository _hostRepository;
    private readonly ILogger<ProxyJumpProfileDialogViewModel> _logger;
    private readonly ProxyJumpProfile? _existingProfile;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private ObservableCollection<JumpHopItemViewModel> _jumpHops = [];

    [ObservableProperty]
    private IReadOnlyList<HostEntry> _availableHosts = [];

    [ObservableProperty]
    private HostEntry? _selectedHostToAdd;

    [ObservableProperty]
    private JumpHopItemViewModel? _selectedHop;

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
    public string Title => IsNewProfile ? "Create ProxyJump Profile" : "Edit ProxyJump Profile";

    /// <summary>
    /// Visual chain preview (Host1 -> Host2 -> Target).
    /// </summary>
    public string ChainPreview
    {
        get
        {
            if (JumpHops.Count == 0)
                return "No jump hosts configured";

            var chain = string.Join(" -> ", JumpHops.OrderBy(h => h.SortOrder).Select(h => h.HostDisplayName));
            return $"You -> {chain} -> [Target]";
        }
    }

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public ProxyJumpProfileDialogViewModel(
        IProxyJumpProfileRepository repository,
        IHostRepository hostRepository,
        ProxyJumpProfile? existingProfile = null,
        ILogger<ProxyJumpProfileDialogViewModel>? logger = null)
    {
        _repository = repository;
        _hostRepository = hostRepository;
        _existingProfile = existingProfile;
        _logger = logger ?? NullLogger<ProxyJumpProfileDialogViewModel>.Instance;

        if (existingProfile != null)
        {
            DisplayName = existingProfile.DisplayName;
            Description = existingProfile.Description;
            IsEnabled = existingProfile.IsEnabled;

            // Load existing hops
            foreach (var hop in existingProfile.JumpHops.OrderBy(h => h.SortOrder))
            {
                JumpHops.Add(JumpHopItemViewModel.FromHop(hop));
            }
        }

        JumpHops.CollectionChanged += OnJumpHopsCollectionChanged;

        _logger.LogDebug("ProxyJumpProfileDialogViewModel initialized for {Mode} profile",
            IsNewProfile ? "new" : "editing");
    }

    private void OnJumpHopsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ChainPreview));
    }

    /// <summary>
    /// Loads available hosts for the jump host selector.
    /// </summary>
    public async Task LoadAvailableHostsAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var hosts = await _hostRepository.GetAllAsync(ct);
            AvailableHosts = hosts;
            _logger.LogDebug("Loaded {HostCount} available hosts for jump host selection", hosts.Count);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddJumpHop))]
    private void AddJumpHop()
    {
        if (SelectedHostToAdd == null) return;

        // Check if this host is already in the chain
        if (JumpHops.Any(h => h.HostId == SelectedHostToAdd.Id))
        {
            ValidationError = "This host is already in the jump chain.";
            return;
        }

        var newHop = JumpHopItemViewModel.FromHost(SelectedHostToAdd, JumpHops.Count);
        JumpHops.Add(newHop);
        SelectedHostToAdd = null;
        ValidationError = null;

        _logger.LogDebug("Added jump hop: {HostDisplayName} at position {SortOrder}",
            newHop.HostDisplayName, newHop.SortOrder);
    }

    private bool CanAddJumpHop() => SelectedHostToAdd != null;

    [RelayCommand]
    private void RemoveJumpHop(JumpHopItemViewModel? hop)
    {
        if (hop == null) return;

        JumpHops.Remove(hop);

        // Reorder remaining hops
        ReorderHops();

        _logger.LogDebug("Removed jump hop: {HostDisplayName}", hop.HostDisplayName);
    }

    [RelayCommand(CanExecute = nameof(CanMoveHopUp))]
    private void MoveHopUp(JumpHopItemViewModel? hop)
    {
        if (hop == null) return;

        var index = JumpHops.IndexOf(hop);
        if (index <= 0) return;

        JumpHops.Move(index, index - 1);
        ReorderHops();

        _logger.LogDebug("Moved hop {HostDisplayName} up to position {NewPosition}",
            hop.HostDisplayName, hop.SortOrder);
    }

    private bool CanMoveHopUp(JumpHopItemViewModel? hop)
    {
        if (hop == null) return false;
        return JumpHops.IndexOf(hop) > 0;
    }

    [RelayCommand(CanExecute = nameof(CanMoveHopDown))]
    private void MoveHopDown(JumpHopItemViewModel? hop)
    {
        if (hop == null) return;

        var index = JumpHops.IndexOf(hop);
        if (index < 0 || index >= JumpHops.Count - 1) return;

        JumpHops.Move(index, index + 1);
        ReorderHops();

        _logger.LogDebug("Moved hop {HostDisplayName} down to position {NewPosition}",
            hop.HostDisplayName, hop.SortOrder);
    }

    private bool CanMoveHopDown(JumpHopItemViewModel? hop)
    {
        if (hop == null) return false;
        var index = JumpHops.IndexOf(hop);
        return index >= 0 && index < JumpHops.Count - 1;
    }

    private void ReorderHops()
    {
        for (int i = 0; i < JumpHops.Count; i++)
        {
            JumpHops[i].SortOrder = i;
        }
        OnPropertyChanged(nameof(ChainPreview));
    }

    partial void OnSelectedHopChanged(JumpHopItemViewModel? value)
    {
        MoveHopUpCommand.NotifyCanExecuteChanged();
        MoveHopDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedHostToAddChanged(HostEntry? value)
    {
        AddJumpHopCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationError = null;

        // Validate
        var errors = ValidateProfile();
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
                _existingProfile.IsEnabled = IsEnabled;
                _existingProfile.UpdatedAt = DateTimeOffset.UtcNow;

                // Replace hops
                _existingProfile.JumpHops.Clear();
                foreach (var hopVm in JumpHops)
                {
                    _existingProfile.JumpHops.Add(hopVm.ToHop(_existingProfile.Id));
                }

                await _repository.UpdateAsync(_existingProfile);
                _logger.LogInformation("Updated ProxyJump profile: {DisplayName}", DisplayName);
            }
            else
            {
                // Create new profile
                var profile = new ProxyJumpProfile
                {
                    DisplayName = DisplayName.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    IsEnabled = IsEnabled
                };

                foreach (var hopVm in JumpHops)
                {
                    profile.JumpHops.Add(hopVm.ToHop(profile.Id));
                }

                await _repository.AddAsync(profile);
                _logger.LogInformation("Created ProxyJump profile: {DisplayName}", DisplayName);
            }

            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save ProxyJump profile");
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

    private List<string> ValidateProfile()
    {
        var errors = new List<string>();

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

        if (JumpHops.Count == 0)
        {
            errors.Add("At least one jump host is required.");
        }

        // Check for circular references (a host cannot be its own jump host)
        // This is a basic check - more advanced circular detection happens in the service layer
        var hostIds = JumpHops.Select(h => h.HostId).ToHashSet();
        if (hostIds.Count != JumpHops.Count)
        {
            errors.Add("Duplicate hosts detected in the jump chain.");
        }

        return errors;
    }

    /// <summary>
    /// Gets the resulting profile after successful save.
    /// Returns null if editing an existing profile (use the existing reference).
    /// </summary>
    public ProxyJumpProfile? GetProfile()
    {
        return _existingProfile;
    }

    public void Dispose()
    {
        JumpHops.CollectionChanged -= OnJumpHopsCollectionChanged;
    }
}
