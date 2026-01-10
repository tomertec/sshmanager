using System.Windows;
using System.Windows.Input;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.Views.Dialogs;

public partial class SnippetManagerDialog : Window
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

        // Bind opacity slider to background transparency
        OpacitySlider.ValueChanged += OnOpacitySliderValueChanged;
    }

    private void OnOpacitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Set window opacity - makes entire window see-through
        Opacity = e.NewValue;
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
        }
        else
        {
            DragMove();
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
