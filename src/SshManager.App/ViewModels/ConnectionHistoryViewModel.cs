using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

public partial class ConnectionHistoryViewModel : ObservableObject
{
    private const int MaxLoadedEntries = 1000;

    private readonly IConnectionHistoryRepository _historyRepo;
    private readonly IHostRepository _hostRepo;
    private readonly ILogger<ConnectionHistoryViewModel> _logger;
    private bool _isApplyingFilter;
    private List<ConnectionHistory> _allEntries = [];

    [ObservableProperty]
    private ObservableCollection<ConnectionHistory> _historyEntries = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalConnections;

    [ObservableProperty]
    private int _successfulConnections;

    [ObservableProperty]
    private int _failedConnections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages), nameof(CanGoPreviousPage), nameof(CanGoNextPage), nameof(PagingSummary))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages), nameof(CanGoPreviousPage), nameof(CanGoNextPage), nameof(PagingSummary))]
    private int _pageSize = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PagingSummary))]
    private int _totalFilteredCount;

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    public event Action? RequestClose;
    public event Func<HostEntry, Task>? OnConnectRequested;

    public IReadOnlyList<string> StatusFilters { get; } = ["All", "Success", "Failed"];

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalFilteredCount / (double)Math.Max(1, PageSize)));

    public bool CanGoPreviousPage => CurrentPage > 1;

    public bool CanGoNextPage => CurrentPage < TotalPages;

    public string PagingSummary => $"Page {CurrentPage} / {TotalPages} ({TotalFilteredCount} entries)";

    public ConnectionHistoryViewModel(
        IConnectionHistoryRepository historyRepo,
        IHostRepository hostRepo,
        ILogger<ConnectionHistoryViewModel>? logger = null)
    {
        _historyRepo = historyRepo;
        _hostRepo = hostRepo;
        _logger = logger ?? NullLogger<ConnectionHistoryViewModel>.Instance;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await _historyRepo.GetRecentAsync(MaxLoadedEntries);
            _allEntries = entries;

            TotalConnections = entries.Count;
            SuccessfulConnections = entries.Count(e => e.WasSuccessful);
            FailedConnections = entries.Count(e => !e.WasSuccessful);

            CurrentPage = 1;
            ApplyFilterAndPaging();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading connection history");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to clear all connection history? This cannot be undone.",
            "Clear History",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await _historyRepo.ClearAllAsync();
            _allEntries.Clear();
            HistoryEntries.Clear();
            TotalConnections = 0;
            SuccessfulConnections = 0;
            FailedConnections = 0;
            TotalFilteredCount = 0;
            CurrentPage = 1;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private void PreviousPage()
    {
        if (!CanGoPreviousPage)
        {
            return;
        }

        CurrentPage--;
    }

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private void NextPage()
    {
        if (!CanGoNextPage)
        {
            return;
        }

        CurrentPage++;
    }

    [RelayCommand]
    private async Task ReconnectAsync(ConnectionHistory? entry)
    {
        if (entry?.Host == null) return;

        // Fetch the latest host data
        var host = await _hostRepo.GetByIdAsync(entry.HostId);
        if (host != null && OnConnectRequested != null)
        {
            await OnConnectRequested.Invoke(host);
            RequestClose?.Invoke();
        }
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }

    partial void OnSelectedStatusFilterChanged(string value)
    {
        CurrentPage = 1;
        ApplyFilterAndPaging();
    }

    partial void OnCurrentPageChanged(int value)
    {
        ApplyFilterAndPaging();
    }

    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        ApplyFilterAndPaging();
    }

    private void ApplyFilterAndPaging()
    {
        if (_isApplyingFilter) return;

        try
        {
            _isApplyingFilter = true;

            IEnumerable<ConnectionHistory> filtered = _allEntries;

            filtered = SelectedStatusFilter switch
            {
                "Success" => filtered.Where(e => e.WasSuccessful),
                "Failed" => filtered.Where(e => !e.WasSuccessful),
                _ => filtered
            };

            var filteredList = filtered.ToList();
            TotalFilteredCount = filteredList.Count;

            var totalPages = TotalPages;
            if (CurrentPage > totalPages)
            {
                CurrentPage = totalPages;
                return;
            }

            var pageEntries = filteredList
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            HistoryEntries = new ObservableCollection<ConnectionHistory>(pageEntries);
            OnPropertyChanged(nameof(PagingSummary));
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            _isApplyingFilter = false;
        }
    }
}
