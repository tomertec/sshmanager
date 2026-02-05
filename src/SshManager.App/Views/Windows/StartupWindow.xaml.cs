using System.Windows;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Windows;

/// <summary>
/// Startup splash window that displays progress during application initialization.
/// </summary>
public partial class StartupWindow : FluentWindow
{
    public StartupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Updates the status message displayed to the user.
    /// </summary>
    /// <param name="status">The status message to display.</param>
    public void UpdateStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateStatus(status));
            return;
        }

        StatusText.Text = status;
    }
}
