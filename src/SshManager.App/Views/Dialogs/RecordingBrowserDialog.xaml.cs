using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for browsing and managing session recordings.
/// </summary>
public partial class RecordingBrowserDialog : FluentWindow
{
    private readonly ILogger<RecordingBrowserDialog> _logger;

    public RecordingBrowserDialog(RecordingBrowserViewModel viewModel, ILogger<RecordingBrowserDialog>? logger = null)
    {
        _logger = logger ?? NullLogger<RecordingBrowserDialog>.Instance;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is RecordingBrowserViewModel viewModel)
            {
                await viewModel.LoadRecordingsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RecordingBrowserDialog.OnLoaded");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
