using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Services;
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
    private Dictionary<Guid, HostConnectionStats> _hostStats = new();

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

    [ObservableProperty]
    private Dictionary<Guid, List<int>> _matchedIndices = new();

    [ObservableProperty]
    private IReadOnlyDictionary<Guid, HostStatus>? _hostStatuses;

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

    /// <summary>
    /// Gets connection stats for a host.
    /// </summary>
    public HostConnectionStats? GetHostStats(Guid hostId)
    {
        return _hostStats.TryGetValue(hostId, out var stats) ? stats : null;
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
            _ = LoadHostStatsAsync();
        }
    }

    private async Task LoadHostStatsAsync()
    {
        try
        {
            _hostStats = await _connectionHistoryRepository.GetAllHostStatsAsync();
        }
        catch
        {
            _hostStats = new Dictionary<Guid, HostConnectionStats>();
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
        MatchedIndices.Clear();

        var searchText = SearchText?.Trim() ?? "";

        IEnumerable<(HostEntry Host, int Score, List<int> Indices)> filtered;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            // Show all hosts when no search text, with favorites first
            filtered = _allHosts
                .OrderByDescending(h => h.IsFavorite)
                .ThenBy(h => h.DisplayName)
                .Select(h => (h, 0, new List<int>()));
        }
        else
        {
            // Filter and score hosts by relevance using fuzzy matching
            filtered = _allHosts
                .Select(h => CalculateFuzzyMatchScore(h, searchText))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Host.IsFavorite)
                .ThenBy(x => x.Host.DisplayName);
        }

        var newIndices = new Dictionary<Guid, List<int>>();
        foreach (var (host, score, indices) in filtered.Take(20)) // Limit to 20 results for performance
        {
            FilteredHosts.Add(host);
            if (indices.Count > 0)
            {
                newIndices[host.Id] = indices;
            }
        }
        MatchedIndices = newIndices;

        // Auto-select first result
        SelectedHost = FilteredHosts.FirstOrDefault();

        OnPropertyChanged(nameof(HasResults));
    }

    private static (HostEntry Host, int Score, List<int> Indices) CalculateFuzzyMatchScore(HostEntry host, string searchText)
    {
        // Use fuzzy matching with weighted fields
        var fields = new List<(string Text, int Weight)>
        {
            (host.DisplayName ?? "", 10),    // Highest weight for display name
            (host.Hostname ?? "", 8),         // High weight for hostname
            (host.Username ?? "", 5),         // Medium weight for username
            (host.Group?.Name ?? "", 3),      // Lower weight for group
            (host.Notes ?? "", 1)             // Lowest weight for notes
        };

        var result = FuzzyMatcher.MatchMultiple(searchText, fields);
        
        // Boost score for favorites
        var finalScore = result.Score;
        if (host.IsFavorite && result.IsMatch)
        {
            finalScore += 20;
        }

        // Return indices for display name highlighting only
        var displayNameMatch = FuzzyMatcher.Match(searchText, host.DisplayName ?? "");
        
        return (host, finalScore, displayNameMatch.IsMatch ? displayNameMatch.MatchedIndices : new List<int>());
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
