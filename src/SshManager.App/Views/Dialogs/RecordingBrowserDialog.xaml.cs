using System.Windows;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Dialogs;

/// <summary>
/// Dialog for browsing and managing session recordings.
/// </summary>
public partial class RecordingBrowserDialog : FluentWindow
{
    public RecordingBrowserDialog(RecordingBrowserViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordingBrowserViewModel viewModel)
        {
            await viewModel.LoadRecordingsAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
