using System.Windows;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

public partial class SnippetManagerDialog : FluentWindow
{
    private readonly SnippetManagerViewModel _viewModel;

    public event Action<CommandSnippet>? OnExecuteSnippet;

    public SnippetManagerDialog()
    {
        var snippetRepo = App.GetService<ISnippetRepository>();
        _viewModel = new SnippetManagerViewModel(snippetRepo);
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.OnExecuteSnippet += OnSnippetExecute;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnRequestClose()
    {
        Close();
    }

    private void OnSnippetExecute(CommandSnippet snippet)
    {
        OnExecuteSnippet?.Invoke(snippet);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.OnExecuteSnippet -= OnSnippetExecute;
        base.OnClosed(e);
    }
}
