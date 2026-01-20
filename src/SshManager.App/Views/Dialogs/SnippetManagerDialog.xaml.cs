using System.Windows;
using System.Windows.Input;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.Views.Dialogs;

public partial class SnippetManagerDialog : Window
{
    private readonly SnippetManagerViewModel _viewModel;
    private readonly ISettingsRepository _settingsRepo;
    private AppSettings? _settings;

    public event Action<CommandSnippet>? OnExecuteSnippet;

    /// <summary>
    /// Initializes a new instance of the SnippetManagerDialog with dependency injection.
    /// </summary>
    /// <param name="viewModel">The snippet manager view model.</param>
    /// <param name="settingsRepo">The settings repository.</param>
    public SnippetManagerDialog(SnippetManagerViewModel viewModel, ISettingsRepository settingsRepo)
    {
        _viewModel = viewModel;
        _settingsRepo = settingsRepo;
        DataContext = _viewModel;

        InitializeComponent();

        _viewModel.RequestClose += OnRequestClose;
        _viewModel.OnExecuteSnippet += OnSnippetExecute;
        Loaded += OnLoaded;

        // Bind opacity slider to background transparency
        OpacitySlider.ValueChanged += OnOpacitySliderValueChanged;
    }

    private async void OnOpacitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Set window opacity - makes entire window see-through
        Opacity = e.NewValue;

        // Save the opacity setting
        if (_settings != null)
        {
            _settings.SnippetManagerOpacity = e.NewValue;
            await _settingsRepo.UpdateAsync(_settings);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Load opacity setting
        _settings = await _settingsRepo.GetAsync();
        OpacitySlider.Value = _settings.SnippetManagerOpacity;
        Opacity = _settings.SnippetManagerOpacity;

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
