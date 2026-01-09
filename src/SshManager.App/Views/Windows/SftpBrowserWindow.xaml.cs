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

        // Cleanup on close
        Closed += async (_, _) =>
        {
            if (viewModel.SftpBrowser is { } browser)
            {
                await browser.DisposeAsync();
            }
        };
    }
}
