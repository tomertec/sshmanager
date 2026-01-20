using System.Windows;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for importing SSH sessions from PuTTY's Windows Registry.
/// </summary>
public partial class PuttyImportDialog : FluentWindow
{
    private readonly PuttyImportViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the PuttyImportDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The PuTTY import view model.</param>
    public PuttyImportDialog(PuttyImportViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-load PuTTY sessions when dialog opens
        _viewModel.LoadSessionsCommand.Execute(null);
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
}
