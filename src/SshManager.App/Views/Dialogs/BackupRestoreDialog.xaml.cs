using System.Windows;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class BackupRestoreDialog : FluentWindow
{
    private readonly BackupRestoreViewModel _viewModel;

    /// <summary>
    /// Event raised when a restore operation completes successfully.
    /// The main window should refresh its host list when this fires.
    /// </summary>
    public event Action? OnRestoreCompleted;

    /// <summary>
    /// Initializes a new instance of the BackupRestoreDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The backup/restore view model.</param>
    public BackupRestoreDialog(BackupRestoreViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.OnRestoreCompleted += OnRestoreCompletedHandler;
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

    private void OnRestoreCompletedHandler()
    {
        OnRestoreCompleted?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.OnRestoreCompleted -= OnRestoreCompletedHandler;
        base.OnClosed(e);
    }
}
