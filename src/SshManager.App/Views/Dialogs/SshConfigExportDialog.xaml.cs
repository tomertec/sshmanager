using System.Windows;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SshConfigExportDialog : FluentWindow
{
    private readonly SshConfigExportDialogViewModel _viewModel;

    public SshConfigExportDialog(SshConfigExportDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
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
}
