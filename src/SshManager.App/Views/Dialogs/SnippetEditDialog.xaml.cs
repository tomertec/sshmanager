using SshManager.App.ViewModels;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SnippetEditDialog : FluentWindow
{
    private readonly SnippetEditViewModel _viewModel;

    public SnippetEditDialog(SnippetEditViewModel viewModel)
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

    public CommandSnippet GetSnippet() => _viewModel.GetSnippet();
}
