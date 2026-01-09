using System.Windows;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Windows;

public partial class SftpBrowserWindow : FluentWindow
{
    public SftpBrowserWindow()
    {
        InitializeComponent();
    }

    public SftpBrowserWindow(SftpBrowserWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Cleanup on close and restore focus to main window
        Closed += async (_, _) =>
        {
            // Activate main window to prevent it from appearing minimized
            Application.Current.MainWindow?.Activate();

            if (viewModel.SftpBrowser is { } browser)
            {
                await browser.DisposeAsync();
            }
        };
    }
}
