using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Terminal;

namespace SshManager.App.ViewModels;

/// <summary>
/// Result type for the session picker dialog.
/// </summary>
public enum SessionPickerResult
{
    Cancelled,
    NewConnection,
    ExistingSession,
    EmptyPane
}

/// <summary>
/// Result data from the session picker dialog.
/// </summary>
public class SessionPickerResultData
{
    public SessionPickerResult Result { get; set; }
    public HostEntry? SelectedHost { get; set; }
    public TerminalSession? SelectedSession { get; set; }
}

/// <summary>
/// ViewModel for the session picker dialog.
/// </summary>
public partial class SessionPickerViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<HostEntry> _hosts = [];

    [ObservableProperty]
    private ObservableCollection<TerminalSession> _activeSessions = [];

    [ObservableProperty]
    private HostEntry? _selectedHost;

    [ObservableProperty]
    private TerminalSession? _selectedSession;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Event raised when dialog should close with result.
    /// </summary>
    public event Action<SessionPickerResultData>? RequestClose;

    public SessionPickerViewModel()
    {
    }

    /// <summary>
    /// Initializes the view model with available hosts and sessions.
    /// </summary>
    public void Initialize(IEnumerable<HostEntry> hosts, IEnumerable<TerminalSession> sessions)
    {
        Hosts = new ObservableCollection<HostEntry>(hosts.OrderBy(h => h.DisplayName));
        ActiveSessions = new ObservableCollection<TerminalSession>(sessions.Where(s => s.IsConnected));
    }

    /// <summary>
    /// Gets the filtered hosts based on search text.
    /// </summary>
    public IEnumerable<HostEntry> FilteredHosts
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return Hosts;

            var search = SearchText.ToLowerInvariant();
            return Hosts.Where(h =>
                h.DisplayName.ToLowerInvariant().Contains(search) ||
                h.Hostname.ToLowerInvariant().Contains(search) ||
                (h.Username?.ToLowerInvariant().Contains(search) ?? false));
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredHosts));
    }

    [RelayCommand]
    private void SelectHost()
    {
        if (SelectedHost == null)
            return;

        RequestClose?.Invoke(new SessionPickerResultData
        {
            Result = SessionPickerResult.NewConnection,
            SelectedHost = SelectedHost
        });
    }

    [RelayCommand]
    private void SelectSession()
    {
        if (SelectedSession == null)
            return;

        RequestClose?.Invoke(new SessionPickerResultData
        {
            Result = SessionPickerResult.ExistingSession,
            SelectedSession = SelectedSession
        });
    }

    [RelayCommand]
    private void CreateEmpty()
    {
        RequestClose?.Invoke(new SessionPickerResultData
        {
            Result = SessionPickerResult.EmptyPane
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(new SessionPickerResultData
        {
            Result = SessionPickerResult.Cancelled
        });
    }

    /// <summary>
    /// Handles double-click on host.
    /// </summary>
    public void OnHostDoubleClick()
    {
        SelectHost();
    }

    /// <summary>
    /// Handles double-click on session.
    /// </summary>
    public void OnSessionDoubleClick()
    {
        SelectSession();
    }
}
