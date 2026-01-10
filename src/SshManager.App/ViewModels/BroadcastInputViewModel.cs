using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel responsible for multi-session broadcast input functionality.
/// Handles broadcast mode toggling and session selection for broadcast input.
/// </summary>
public partial class BroadcastInputViewModel : ObservableObject, IDisposable
{
    private readonly ITerminalSessionManager _sessionManager;
    private readonly IBroadcastInputService _broadcastService;
    private readonly ILogger<BroadcastInputViewModel> _logger;

    public BroadcastInputViewModel(
        ITerminalSessionManager sessionManager,
        IBroadcastInputService broadcastService,
        ILogger<BroadcastInputViewModel>? logger = null)
    {
        _sessionManager = sessionManager;
        _broadcastService = broadcastService;
        _logger = logger ?? NullLogger<BroadcastInputViewModel>.Instance;

        // Subscribe to broadcast mode changes from the session manager
        _sessionManager.BroadcastModeChanged += OnBroadcastModeChanged;

        _logger.LogDebug("BroadcastInputViewModel initialized");
    }

    private void OnBroadcastModeChanged(object? sender, bool isEnabled)
    {
        OnPropertyChanged(nameof(IsBroadcastMode));
        OnPropertyChanged(nameof(BroadcastSelectedCount));
    }

    /// <summary>
    /// Gets or sets whether broadcast input mode is enabled.
    /// When enabled, keyboard input is sent to all selected sessions.
    /// </summary>
    public bool IsBroadcastMode
    {
        get => _sessionManager.IsBroadcastMode;
        set
        {
            if (_sessionManager.IsBroadcastMode != value)
            {
                _sessionManager.IsBroadcastMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BroadcastSelectedCount));
            }
        }
    }

    /// <summary>
    /// Gets the number of sessions selected for broadcast input.
    /// </summary>
    public int BroadcastSelectedCount => _sessionManager.BroadcastSelectedCount;

    /// <summary>
    /// Gets the broadcast input service for the terminal control.
    /// </summary>
    public IBroadcastInputService BroadcastService => _broadcastService;

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

    public void Dispose()
    {
        _sessionManager.BroadcastModeChanged -= OnBroadcastModeChanged;
    }
}
