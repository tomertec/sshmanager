using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for managing active port forwardings and port forwarding profiles.
/// </summary>
public partial class PortForwardingManagerViewModel : ObservableObject, IDisposable
{
    private readonly IPortForwardingProfileRepository _repository;
    private readonly ILogger<PortForwardingManagerViewModel> _logger;

    /// <summary>
    /// All active port forwardings across all sessions.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ActivePortForwardingViewModel> _activeForwardings = [];

    /// <summary>
    /// All port forwarding profiles.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PortForwardingProfile> _profiles = [];

    /// <summary>
    /// Currently selected active forwarding.
    /// </summary>
    [ObservableProperty]
    private ActivePortForwardingViewModel? _selectedForwarding;

    /// <summary>
    /// Currently selected profile.
    /// </summary>
    [ObservableProperty]
    private PortForwardingProfile? _selectedProfile;

    /// <summary>
    /// Whether data is loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Number of active forwardings.
    /// </summary>
    public int ActiveCount => ActiveForwardings.Count(f => f.IsActive);

    /// <summary>
    /// Whether there are any active forwardings.
    /// </summary>
    public bool HasActiveForwardings => ActiveForwardings.Any(f => f.IsActive);

    /// <summary>
    /// Summary text for active forwardings (e.g., "3 active").
    /// </summary>
    public string ActiveSummary => ActiveCount switch
    {
        0 => "No active forwards",
        1 => "1 active forward",
        _ => $"{ActiveCount} active forwards"
    };

    /// <summary>
    /// Whether there are any configured profiles.
    /// </summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Event raised when a port forwarding needs to be started.
    /// </summary>
    public event EventHandler<StartForwardingEventArgs>? StartForwardingRequested;

    /// <summary>
    /// Event raised when a port forwarding needs to be stopped.
    /// </summary>
    public event EventHandler<StopForwardingEventArgs>? StopForwardingRequested;

    public PortForwardingManagerViewModel(
        IPortForwardingProfileRepository repository,
        ILogger<PortForwardingManagerViewModel>? logger = null)
    {
        _repository = repository;
        _logger = logger ?? NullLogger<PortForwardingManagerViewModel>.Instance;

        ActiveForwardings.CollectionChanged += OnActiveForwardingsCollectionChanged;

        _logger.LogDebug("PortForwardingManagerViewModel initialized");
    }

    private void OnActiveForwardingsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActiveForwardings));
        OnPropertyChanged(nameof(ActiveSummary));
    }

    /// <summary>
    /// Loads all port forwarding profiles.
    /// </summary>
    public async Task LoadProfilesAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var profiles = await _repository.GetAllAsync(ct);
            Profiles = new ObservableCollection<PortForwardingProfile>(profiles);
            OnPropertyChanged(nameof(HasProfiles));
            _logger.LogInformation("Loaded {ProfileCount} port forwarding profiles", profiles.Count);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads port forwarding profiles for a specific host.
    /// </summary>
    public async Task<IReadOnlyList<PortForwardingProfile>> GetProfilesForHostAsync(
        Guid hostId,
        CancellationToken ct = default)
    {
        var profiles = await _repository.GetByHostIdAsync(hostId, ct);
        _logger.LogDebug("Found {ProfileCount} port forwarding profiles for host {HostId}",
            profiles.Count, hostId);
        return profiles;
    }

    /// <summary>
    /// Gets global port forwarding profiles (not associated with any host).
    /// </summary>
    public async Task<IReadOnlyList<PortForwardingProfile>> GetGlobalProfilesAsync(
        CancellationToken ct = default)
    {
        var profiles = await _repository.GetGlobalProfilesAsync(ct);
        _logger.LogDebug("Found {ProfileCount} global port forwarding profiles", profiles.Count);
        return profiles;
    }

    /// <summary>
    /// Adds an active forwarding to the manager.
    /// </summary>
    public void AddActiveForwarding(ActivePortForwardingViewModel forwarding)
    {
        ActiveForwardings.Add(forwarding);
        _logger.LogDebug("Added active forwarding: {DisplayName} for session {SessionId}",
            forwarding.DisplayName, forwarding.SessionId);
    }

    /// <summary>
    /// Removes an active forwarding from the manager.
    /// </summary>
    public void RemoveActiveForwarding(ActivePortForwardingViewModel forwarding)
    {
        ActiveForwardings.Remove(forwarding);
        _logger.LogDebug("Removed active forwarding: {DisplayName}", forwarding.DisplayName);
    }

    /// <summary>
    /// Finds an active forwarding by ID.
    /// </summary>
    public ActivePortForwardingViewModel? FindActiveForwarding(Guid id)
    {
        return ActiveForwardings.FirstOrDefault(f => f.Id == id);
    }

    /// <summary>
    /// Gets all active forwardings for a session.
    /// </summary>
    public IReadOnlyList<ActivePortForwardingViewModel> GetForwardingsForSession(Guid sessionId)
    {
        return ActiveForwardings.Where(f => f.SessionId == sessionId).ToList();
    }

    /// <summary>
    /// Requests starting a port forwarding.
    /// </summary>
    [RelayCommand]
    private void StartForwarding(PortForwardingProfile? profile)
    {
        if (profile == null) return;

        StartForwardingRequested?.Invoke(this, new StartForwardingEventArgs(profile));
        _logger.LogDebug("Requested start of forwarding: {DisplayName}", profile.DisplayName);
    }

    /// <summary>
    /// Requests stopping an active forwarding.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopForwarding))]
    private void StopForwarding(ActivePortForwardingViewModel? forwarding)
    {
        if (forwarding == null || !forwarding.CanStop) return;

        StopForwardingRequested?.Invoke(this, new StopForwardingEventArgs(forwarding));
        _logger.LogDebug("Requested stop of forwarding: {DisplayName}", forwarding.DisplayName);
    }

    private bool CanStopForwarding(ActivePortForwardingViewModel? forwarding)
    {
        return forwarding?.CanStop == true;
    }

    /// <summary>
    /// Stops all active forwardings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveForwardings))]
    private void StopAll()
    {
        var toStop = ActiveForwardings.Where(f => f.CanStop).ToList();
        foreach (var forwarding in toStop)
        {
            StopForwardingRequested?.Invoke(this, new StopForwardingEventArgs(forwarding));
        }
        _logger.LogInformation("Requested stop of {Count} active forwardings", toStop.Count);
    }

    /// <summary>
    /// Stops all forwardings for a specific session.
    /// </summary>
    public void StopAllForSession(Guid sessionId)
    {
        var toStop = ActiveForwardings
            .Where(f => f.SessionId == sessionId && f.CanStop)
            .ToList();

        foreach (var forwarding in toStop)
        {
            forwarding.MarkStopped();
            ActiveForwardings.Remove(forwarding);
        }

        _logger.LogInformation("Stopped {Count} forwardings for session {SessionId}",
            toStop.Count, sessionId);
    }

    /// <summary>
    /// Deletes a port forwarding profile.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfileAsync(PortForwardingProfile? profile)
    {
        if (profile == null) return;

        try
        {
            await _repository.DeleteAsync(profile.Id);
            Profiles.Remove(profile);
            OnPropertyChanged(nameof(HasProfiles));
            _logger.LogInformation("Deleted port forwarding profile: {DisplayName}", profile.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete port forwarding profile: {DisplayName}", profile.DisplayName);
        }
    }

    partial void OnSelectedForwardingChanged(ActivePortForwardingViewModel? value)
    {
        StopForwardingCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        ActiveForwardings.CollectionChanged -= OnActiveForwardingsCollectionChanged;
    }
}

/// <summary>
/// Event args for requesting to start a port forwarding.
/// </summary>
public sealed class StartForwardingEventArgs : EventArgs
{
    public PortForwardingProfile Profile { get; }

    public StartForwardingEventArgs(PortForwardingProfile profile)
    {
        Profile = profile;
    }
}

/// <summary>
/// Event args for requesting to stop a port forwarding.
/// </summary>
public sealed class StopForwardingEventArgs : EventArgs
{
    public ActivePortForwardingViewModel Forwarding { get; }

    public StopForwardingEventArgs(ActivePortForwardingViewModel forwarding)
    {
        Forwarding = forwarding;
    }
}
