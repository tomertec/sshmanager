using System.Windows;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class PassphrasePromptDialog : FluentWindow
{
    private readonly PassphrasePromptViewModel _viewModel;

    public PassphrasePromptDialog(PassphrasePromptViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;

        // Focus the passphrase box when loaded
        Loaded += (s, e) => PassphraseBox.Focus();
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    private void PassphraseBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            _viewModel.Passphrase = passwordBox.Password;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
