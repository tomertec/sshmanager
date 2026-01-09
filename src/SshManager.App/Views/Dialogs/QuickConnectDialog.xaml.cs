using System.ComponentModel;
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

        // Subscribe to connection type changes to update focus
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Focus appropriate field when loaded
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FocusAppropriateField();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickConnectViewModel.ConnectionType))
        {
            // Delay focus to allow UI to update
            Dispatcher.BeginInvoke(new Action(FocusAppropriateField), System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    private void FocusAppropriateField()
    {
        if (_viewModel.IsSshMode)
        {
            HostnameBox.Focus();
        }
        else
        {
            SerialPortCombo.Focus();
        }
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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }
}
