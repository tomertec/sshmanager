using System.Windows;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for playing back recorded terminal sessions.
/// </summary>
public partial class RecordingPlaybackDialog : FluentWindow
{
    public RecordingPlaybackDialog(RecordingPlaybackViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Provide terminal control reference to viewmodel
        viewModel.SetTerminalControl(TerminalControl);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordingPlaybackViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is RecordingPlaybackViewModel viewModel)
        {
            await viewModel.CleanupAsync();
        }
    }
}
