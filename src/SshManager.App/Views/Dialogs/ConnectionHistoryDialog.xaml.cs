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

    /// <summary>
    /// Initializes a new instance of the ConnectionHistoryDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The connection history view model.</param>
    public ConnectionHistoryDialog(ConnectionHistoryViewModel viewModel)
    {
        _viewModel = viewModel;
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
