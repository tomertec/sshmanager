using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Interaction logic for SerialQuickConnectDialog.xaml
/// </summary>
public partial class SerialQuickConnectDialog : FluentWindow
{
    public SerialQuickConnectViewModel ViewModel { get; }

    public SerialQuickConnectDialog(SerialQuickConnectViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose()
    {
        DialogResult = ViewModel.DialogResult;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
