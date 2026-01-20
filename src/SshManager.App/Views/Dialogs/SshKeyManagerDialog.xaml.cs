using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SshManager.App.ViewModels;
using SshManager.Security;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SshKeyManagerDialog : FluentWindow
{
    private readonly SshKeyManagerViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the SshKeyManagerDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The SSH key manager view model.</param>
    /// <param name="serviceProvider">The service provider for resolving additional dependencies.</param>
    public SshKeyManagerDialog(SshKeyManagerViewModel viewModel, IServiceProvider serviceProvider)
    {
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.RequestGenerateKey += OnRequestGenerateKey;
        _viewModel.RequestImportPpk += OnRequestImportPpk;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadKeysAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.SelectedKey))
        {
            UpdateActionPanel();
        }
    }

    private void UpdateActionPanel()
    {
        if (_viewModel.SelectedKey != null)
        {
            ActionPanel.Visibility = Visibility.Visible;
            SelectedKeyName.Text = _viewModel.SelectedKey.DisplayName;
            SelectedKeyPath.Text = _viewModel.SelectedKey.PrivateKeyPath;
            UpdateTrackButton();
        }
        else
        {
            ActionPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateTrackButton()
    {
        if (_viewModel.SelectedKey == null) return;

        TrackButton.Content = _viewModel.SelectedKey.IsTracked ? "Untrack" : "Track";
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    private Task OnRequestGenerateKey()
    {
        var keyManager = _serviceProvider.GetRequiredService<ISshKeyManager>();
        var logger = _serviceProvider.GetRequiredService<ILogger<KeyGenerationViewModel>>();
        var generateVm = new KeyGenerationViewModel(keyManager, logger);
        var dialog = new KeyGenerationDialog(generateVm)
        {
            Owner = this
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    private Task OnRequestImportPpk()
    {
        var ppkConverter = _serviceProvider.GetRequiredService<IPpkConverter>();
        var keyManager = _serviceProvider.GetRequiredService<ISshKeyManager>();
        var managedKeyRepo = _serviceProvider.GetRequiredService<Data.Repositories.IManagedKeyRepository>();
        var encryptionService = _serviceProvider.GetService<IKeyEncryptionService>();
        var agentKeyService = _serviceProvider.GetService<Terminal.Services.IAgentKeyService>();
        var logger = _serviceProvider.GetRequiredService<ILogger<PpkImportWizardViewModel>>();

        var wizardVm = new PpkImportWizardViewModel(
            ppkConverter,
            keyManager,
            managedKeyRepo,
            encryptionService,
            agentKeyService,
            logger);

        var dialog = new PpkImportWizardDialog(wizardVm)
        {
            Owner = this
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.LoadKeysAsync();
    }

    private async void TrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedKey == null) return;

        if (_viewModel.SelectedKey.IsTracked)
        {
            await _viewModel.UntrackKeyCommand.ExecuteAsync(null);
        }
        else
        {
            await _viewModel.TrackKeyCommand.ExecuteAsync(null);
        }

        UpdateTrackButton();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.RequestGenerateKey -= OnRequestGenerateKey;
        _viewModel.RequestImportPpk -= OnRequestImportPpk;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosed(e);
    }
}
