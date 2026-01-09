using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the Quick Connect overlay.
/// Provides a searchable list of hosts for quick connection via Ctrl+K.
/// </summary>
public partial class QuickConnectOverlayViewModel : ObservableObject
{
    private readonly List<HostEntry> _allHosts = new();
    private readonly IConnectionHistoryRepository _connectionHistoryRepository;

    /// <summary>
    /// Event raised when a host is selected for connection.
    /// </summary>
    public event EventHandler<HostEntry>? HostSelected;

    /// <summary>
    /// Event raised when the overlay should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults), nameof(ShowRecentSection))]
    private string _searchText = "";

    [ObservableProperty]
    private ObservableCollection<HostEntry> _filteredHosts = new();

    [ObservableProperty]
    private ObservableCollection<HostEntry> _recentHosts = new();

    [ObservableProperty]
    private HostEntry? _selectedHost;

    /// <summary>
    /// Gets whether there are search results to display.
    /// </summary>
    public bool HasResults => FilteredHosts.Count > 0;

    /// <summary>
    /// Gets whether the recent section should be shown.
    /// </summary>
    public bool ShowRecentSection => string.IsNullOrWhiteSpace(SearchText) && RecentHosts.Count > 0;

    public QuickConnectOverlayViewModel(IConnectionHistoryRepository connectionHistoryRepository)
    {
        _connectionHistoryRepository = connectionHistoryRepository;
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterHosts();
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            // Reset state when opening
            SearchText = "";
            FilterHosts();
            _ = LoadRecentHostsAsync();
        }
    }

    /// <summary>
    /// Updates the list of available hosts.
    /// </summary>
    public void SetHosts(IEnumerable<HostEntry> hosts)
    {
        _allHosts.Clear();
        _allHosts.AddRange(hosts);
        FilterHosts();
    }

    /// <summary>
    /// Opens the overlay.
    /// </summary>
    [RelayCommand]
    public void Open()
    {
        IsOpen = true;
    }

    /// <summary>
    /// Closes the overlay.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects the currently highlighted host and triggers connection.
    /// </summary>
    [RelayCommand]
    public void SelectHost()
    {
        if (SelectedHost != null)
        {
            var host = SelectedHost;
            Close();
            HostSelected?.Invoke(this, host);
        }
    }

    /// <summary>
    /// Connects to a specific host.
    /// </summary>
    [RelayCommand]
    public void ConnectToHost(HostEntry? host)
    {
        if (host != null)
        {
            Close();
            HostSelected?.Invoke(this, host);
        }
    }

    /// <summary>
    /// Moves selection to the next host in the list.
    /// </summary>
    [RelayCommand]
    public void SelectNext()
    {
        if (FilteredHosts.Count == 0) return;

        var currentIndex = SelectedHost != null ? FilteredHosts.IndexOf(SelectedHost) : -1;
        var nextIndex = (currentIndex + 1) % FilteredHosts.Count;
        SelectedHost = FilteredHosts[nextIndex];
    }

    /// <summary>
    /// Moves selection to the previous host in the list.
    /// </summary>
    [RelayCommand]
    public void SelectPrevious()
    {
        if (FilteredHosts.Count == 0) return;

        var currentIndex = SelectedHost != null ? FilteredHosts.IndexOf(SelectedHost) : 0;
        var prevIndex = currentIndex <= 0 ? FilteredHosts.Count - 1 : currentIndex - 1;
        SelectedHost = FilteredHosts[prevIndex];
    }

    private void FilterHosts()
    {
        FilteredHosts.Clear();

        var searchLower = SearchText?.Trim().ToLowerInvariant() ?? "";

        IEnumerable<HostEntry> filtered;

        if (string.IsNullOrWhiteSpace(searchLower))
        {
            // Show all hosts when no search text
            filtered = _allHosts.OrderBy(h => h.DisplayName);
        }
        else
        {
            // Filter and score hosts by relevance
            filtered = _allHosts
                .Select(h => new
                {
                    Host = h,
                    Score = CalculateMatchScore(h, searchLower)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Host.DisplayName)
                .Select(x => x.Host);
        }

        foreach (var host in filtered.Take(20)) // Limit to 20 results for performance
        {
            FilteredHosts.Add(host);
        }

        // Auto-select first result
        SelectedHost = FilteredHosts.FirstOrDefault();

        OnPropertyChanged(nameof(HasResults));
    }

    private static int CalculateMatchScore(HostEntry host, string searchLower)
    {
        int score = 0;

        // Exact match on display name
        if (host.DisplayName?.ToLowerInvariant() == searchLower)
            score += 100;
        // Display name starts with search
        else if (host.DisplayName?.ToLowerInvariant().StartsWith(searchLower) == true)
            score += 50;
        // Display name contains search
        else if (host.DisplayName?.ToLowerInvariant().Contains(searchLower) == true)
            score += 25;

        // Hostname match
        if (host.Hostname?.ToLowerInvariant().Contains(searchLower) == true)
            score += 20;

        // Username match
        if (host.Username?.ToLowerInvariant().Contains(searchLower) == true)
            score += 10;

        // Group name match
        if (host.Group?.Name?.ToLowerInvariant().Contains(searchLower) == true)
            score += 5;

        // Notes match
        if (host.Notes?.ToLowerInvariant().Contains(searchLower) == true)
            score += 2;

        return score;
    }

    private async Task LoadRecentHostsAsync()
    {
        try
        {
            var recentHosts = await _connectionHistoryRepository.GetRecentUniqueHostsAsync(5);
            RecentHosts.Clear();
            foreach (var host in recentHosts)
            {
                RecentHosts.Add(host);
            }
            OnPropertyChanged(nameof(ShowRecentSection));
        }
        catch
        {
            // Silently fail if unable to load recent hosts
            RecentHosts.Clear();
        }
    }
}
