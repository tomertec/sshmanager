using System.Windows;
using SshManager.App.ViewModels;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;
using Wpf.Ui.Controls;
using Microsoft.Extensions.Logging;

namespace SshManager.App.Views.Dialogs;

public partial class SettingsDialog : FluentWindow
{
    private readonly SettingsViewModel _viewModel;
    private readonly MainWindowViewModel _mainViewModel;

    public SettingsDialog()
    {
        // Get services from DI
        var settingsRepo = App.GetService<ISettingsRepository>();
        var historyRepo = App.GetService<IConnectionHistoryRepository>();
        var credentialCache = App.GetService<ICredentialCache>();
        var themeService = App.GetService<ITerminalThemeService>();
        _mainViewModel = App.GetService<MainWindowViewModel>();

        _viewModel = new SettingsViewModel(settingsRepo, historyRepo, credentialCache, themeService);
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        await _mainViewModel.ImportHostsAsync();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        await _mainViewModel.ExportHostsAsync();
    }

    private async void ImportSshConfigButton_Click(object sender, RoutedEventArgs e)
    {
        await _mainViewModel.ImportFromSshConfigAsync();
    }

    private void ManageSshKeysButton_Click(object sender, RoutedEventArgs e)
    {
        var keyManager = App.GetService<ISshKeyManager>();
        var managedKeyRepo = App.GetService<IManagedKeyRepository>();
        var logger = App.GetLogger<SshKeyManagerViewModel>();
        var viewModel = new SshKeyManagerViewModel(keyManager, managedKeyRepo, logger);
        var dialog = new SshKeyManagerDialog(viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void BackupManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BackupRestoreDialog
        {
            Owner = this
        };
        dialog.OnRestoreCompleted += () =>
        {
            // Refresh the host list in main window when restore completes
            _ = _mainViewModel.RefreshHostsAsync();
        };
        dialog.ShowDialog();
    }

    private void CloudSyncButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CloudSyncSetupDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
