using System.Windows;
using System.Windows.Controls;
using SshManager.App.ViewModels.Dialogs;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class QuickConnectDialog : FluentWindow
{
    private readonly QuickConnectViewModel _viewModel;

    public QuickConnectDialog(QuickConnectViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // Subscribe to close request
        _viewModel.RequestClose += OnRequestClose;

        // Focus hostname box when loaded
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HostnameBox.Focus();
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            _viewModel.Password = passwordBox.Password;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
