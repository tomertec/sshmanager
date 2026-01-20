using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SettingsDialog : FluentWindow
{
    private readonly SettingsViewModel _viewModel;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the SettingsDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The settings view model.</param>
    /// <param name="mainViewModel">The main window view model.</param>
    /// <param name="serviceProvider">The service provider for resolving additional dependencies.</param>
    public SettingsDialog(
        SettingsViewModel viewModel,
        MainWindowViewModel mainViewModel,
        IServiceProvider serviceProvider)
    {
        _viewModel = viewModel;
        _mainViewModel = mainViewModel;
        _serviceProvider = serviceProvider;
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.RequestManageSshKeys += () => ManageSshKeysButton_Click(null!, null!);
        _viewModel.RequestBackupManager += () => BackupManagerButton_Click(null!, null!);
        _viewModel.RequestImportHosts += async () => await Task.Run(() => ImportButton_Click(null!, null!));
        _viewModel.RequestExportHosts += async () => await Task.Run(() => ExportButton_Click(null!, null!));
        _viewModel.RequestImportSshConfig += async () => await Task.Run(() => ImportSshConfigButton_Click(null!, null!));
        _viewModel.RequestExportSshConfig += async () => await Task.Run(() => ExportSshConfigButton_Click(null!, null!));
        _viewModel.RequestCloudSync += () => CloudSyncButton_Click(null!, null!);
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
        // Event handlers are lambda expressions, don't need explicit removal
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

    private async void ExportSshConfigButton_Click(object sender, RoutedEventArgs e)
    {
        await _mainViewModel.ExportToSshConfigAsync();
    }

    private void ManageSshKeysButton_Click(object sender, RoutedEventArgs e)
    {
        var keyManager = _serviceProvider.GetRequiredService<ISshKeyManager>();
        var managedKeyRepo = _serviceProvider.GetRequiredService<IManagedKeyRepository>();
        var ppkConverter = _serviceProvider.GetRequiredService<IPpkConverter>();
        var logger = _serviceProvider.GetRequiredService<ILogger<SshKeyManagerViewModel>>();
        var viewModel = new SshKeyManagerViewModel(keyManager, managedKeyRepo, ppkConverter, logger);
        var dialog = new SshKeyManagerDialog(viewModel, _serviceProvider)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void BackupManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var backupService = _serviceProvider.GetRequiredService<IBackupService>();
        var viewModel = new BackupRestoreViewModel(backupService);
        var dialog = new BackupRestoreDialog(viewModel)
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
        var cloudSyncService = _serviceProvider.GetRequiredService<ICloudSyncService>();
        var cloudSyncHostedService = _serviceProvider.GetRequiredService<CloudSyncHostedService>();
        var settingsRepo = _serviceProvider.GetRequiredService<ISettingsRepository>();
        var oneDriveDetector = _serviceProvider.GetRequiredService<IOneDrivePathDetector>();

        var viewModel = new CloudSyncSetupViewModel(
            cloudSyncService,
            cloudSyncHostedService,
            settingsRepo,
            oneDriveDetector);

        var dialog = new CloudSyncSetupDialog(viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
