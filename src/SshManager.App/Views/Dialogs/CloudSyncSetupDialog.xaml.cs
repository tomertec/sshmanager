using System.Windows;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.Data.Repositories;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class CloudSyncSetupDialog : FluentWindow
{
    private readonly CloudSyncSetupViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the CloudSyncSetupDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The cloud sync setup view model.</param>
    public CloudSyncSetupDialog(CloudSyncSetupViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.GetPassphrase = GetPassphrase;
        _viewModel.GetConfirmPassphrase = GetConfirmPassphrase;
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

    private string GetPassphrase()
    {
        return PassphraseBox.Password;
    }

    private string GetConfirmPassphrase()
    {
        return ConfirmPassphraseBox.Password;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
