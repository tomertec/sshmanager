using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

public partial class ConnectionHistoryViewModel : ObservableObject
{
    private readonly IConnectionHistoryRepository _historyRepo;
    private readonly IHostRepository _hostRepo;

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

    public event Action? RequestClose;
    public event Func<HostEntry, Task>? OnConnectRequested;

    public ConnectionHistoryViewModel(IConnectionHistoryRepository historyRepo, IHostRepository hostRepo)
    {
        _historyRepo = historyRepo;
        _hostRepo = hostRepo;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await _historyRepo.GetRecentAsync(100);
            HistoryEntries = new ObservableCollection<ConnectionHistory>(entries);

            TotalConnections = entries.Count;
            SuccessfulConnections = entries.Count(e => e.WasSuccessful);
            FailedConnections = entries.Count(e => !e.WasSuccessful);
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
            HistoryEntries.Clear();
            TotalConnections = 0;
            SuccessfulConnections = 0;
            FailedConnections = 0;
        }
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
}
