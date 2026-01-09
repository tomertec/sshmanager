using System.Windows;
using System.Windows.Input;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for selecting a session when splitting panes.
/// </summary>
public partial class SessionPickerDialog : FluentWindow
{
    private readonly SessionPickerViewModel _viewModel;

    public SessionPickerResultData? Result { get; private set; }

    public SessionPickerDialog(SessionPickerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(SessionPickerResultData result)
    {
        Result = result;
        DialogResult = result.Result != SessionPickerResult.Cancelled;
        Close();
    }

    private void HostList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.OnHostDoubleClick();
    }

    private void SessionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.OnSessionDoubleClick();
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        // Determine which tab is active and call the appropriate command
        if (_viewModel.SelectedTabIndex == 0)
        {
            _viewModel.SelectHostCommand.Execute(null);
        }
        else
        {
            _viewModel.SelectSessionCommand.Execute(null);
        }
    }
}
