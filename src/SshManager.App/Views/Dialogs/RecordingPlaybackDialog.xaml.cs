using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for playing back recorded terminal sessions.
/// </summary>
public partial class RecordingPlaybackDialog : FluentWindow
{
    private readonly ILogger<RecordingPlaybackDialog> _logger;

    public RecordingPlaybackDialog(RecordingPlaybackViewModel viewModel, ILogger<RecordingPlaybackDialog>? logger = null)
    {
        _logger = logger ?? NullLogger<RecordingPlaybackDialog>.Instance;
        InitializeComponent();
        DataContext = viewModel;

        // Provide terminal control reference to viewmodel
        viewModel.SetTerminalControl(TerminalControl);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is RecordingPlaybackViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RecordingPlaybackDialog.OnLoaded");
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (DataContext is RecordingPlaybackViewModel viewModel)
            {
                await viewModel.CleanupAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RecordingPlaybackDialog.OnClosing");
        }
    }
}
