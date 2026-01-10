using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class FilePropertiesDialog : FluentWindow
{
    private readonly FilePropertiesDialogViewModel _viewModel;

    public FilePropertiesDialog(FilePropertiesDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Set the icon based on whether it's a directory
        FileIcon.Symbol = _viewModel.IsDirectory
            ? Wpf.Ui.Controls.SymbolRegular.Folder24
            : Wpf.Ui.Controls.SymbolRegular.Document24;
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
}
