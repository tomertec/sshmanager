using System.Windows;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class ConnectionHistoryDialog : FluentWindow
{
    private readonly ConnectionHistoryViewModel _viewModel;

    public event Func<HostEntry, Task>? OnConnectRequested;

    public ConnectionHistoryDialog()
    {
        // Get services from DI
        var historyRepo = App.GetService<IConnectionHistoryRepository>();
        var hostRepo = App.GetService<IHostRepository>();

        _viewModel = new ConnectionHistoryViewModel(historyRepo, hostRepo);
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.OnConnectRequested += OnConnectRequestedHandler;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnRequestClose()
    {
        Close();
    }

    private async Task OnConnectRequestedHandler(HostEntry host)
    {
        if (OnConnectRequested != null)
        {
            await OnConnectRequested.Invoke(host);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.OnConnectRequested -= OnConnectRequestedHandler;
        base.OnClosed(e);
    }
}
