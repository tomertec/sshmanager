using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class RenameDialog : FluentWindow
{
    private readonly RenameDialogViewModel _viewModel;

    public RenameDialog(RenameDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;

        // Select the filename (without extension) when dialog opens
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        NameTextBox.Focus();

        // Select just the name part (without extension) for better UX
        var name = _viewModel.NewName;
        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0)
        {
            NameTextBox.Select(0, lastDot);
        }
        else
        {
            NameTextBox.SelectAll();
        }
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        Loaded -= OnLoaded;
        base.OnClosed(e);
    }

    public string GetNewName() => _viewModel.GetNewName();
}
