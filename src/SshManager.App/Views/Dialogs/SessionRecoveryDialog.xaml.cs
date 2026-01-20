using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SessionRecoveryDialog : FluentWindow
{
    private readonly SessionRecoveryViewModel _viewModel;

    public SessionRecoveryDialog(SessionRecoveryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.ShouldRestore;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
