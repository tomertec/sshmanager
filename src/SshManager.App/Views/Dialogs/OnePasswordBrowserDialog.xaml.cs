using System.Windows.Input;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class OnePasswordBrowserDialog : FluentWindow
{
    private readonly OnePasswordBrowserViewModel _viewModel;

    public OnePasswordBrowserDialog(OnePasswordBrowserViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;

        // Load vaults when dialog opens
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    /// <summary>
    /// Gets the selected op:// reference after dialog closes, or null if cancelled.
    /// </summary>
    public string? SelectedReference => _viewModel.SelectedReference;

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
        {
            _viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
}
