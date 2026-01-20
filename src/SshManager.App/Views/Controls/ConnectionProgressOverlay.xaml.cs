using System.Windows.Controls;

namespace SshManager.App.Views.Controls;

/// <summary>
/// A modal overlay that displays connection progress with a spinner and host name.
/// </summary>
public partial class ConnectionProgressOverlay : UserControl
{
    public ConnectionProgressOverlay()
    {
        InitializeComponent();
    }
}
