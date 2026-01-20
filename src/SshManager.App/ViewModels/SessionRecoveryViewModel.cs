using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the session recovery dialog.
/// Displays sessions that were active when the application crashed.
/// </summary>
public partial class SessionRecoveryViewModel : ObservableObject
{
    /// <summary>
    /// Event raised when the dialog should close.
    /// </summary>
    public event Action? RequestClose;

    /// <summary>
    /// Gets or sets whether the user chose to restore sessions.
    /// </summary>
    public bool ShouldRestore { get; private set; }

    /// <summary>
    /// Gets the sessions available for recovery.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SavedSession> _sessions = [];

    /// <summary>
    /// Gets the session count text for display.
    /// </summary>
    public string SessionCountText => Sessions.Count switch
    {
        1 => "1 session can be restored",
        _ => $"{Sessions.Count} sessions can be restored"
    };

    public SessionRecoveryViewModel(IEnumerable<SavedSession> sessions)
    {
        _sessions = new ObservableCollection<SavedSession>(sessions);
    }

    [RelayCommand]
    private void Restore()
    {
        ShouldRestore = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void DontRestore()
    {
        ShouldRestore = false;
        RequestClose?.Invoke();
    }
}
