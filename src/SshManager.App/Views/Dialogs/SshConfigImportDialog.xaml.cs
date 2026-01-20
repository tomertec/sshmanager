using System.Windows;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SshConfigImportDialog : FluentWindow
{
    private readonly SshConfigImportViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the SshConfigImportDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The SSH config import view model.</param>
    public SshConfigImportDialog(SshConfigImportViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-parse if default config exists
        if (System.IO.File.Exists(_viewModel.ConfigFilePath))
        {
            await _viewModel.ParseConfigCommand.ExecuteAsync(null);
        }
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

    /// <summary>
    /// Gets the hosts selected for import.
    /// </summary>
    public List<HostEntry> GetSelectedHosts()
    {
        return _viewModel.GetSelectedHosts();
    }

    /// <summary>
    /// Gets the selected hosts as import items with full advanced configuration data.
    /// </summary>
    public List<SshConfigImportItem> GetSelectedImportItems()
    {
        return _viewModel.GetSelectedImportItems();
    }
}
