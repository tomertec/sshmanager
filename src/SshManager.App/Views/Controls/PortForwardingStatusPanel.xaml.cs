using System.Windows;
using System.Windows.Controls;

namespace SshManager.App.Views.Controls;

public partial class PortForwardingStatusPanel : UserControl
{
    /// <summary>
    /// Event raised when the user wants to manage port forwarding profiles.
    /// </summary>
    public event EventHandler? ManageProfilesRequested;

    public PortForwardingStatusPanel()
    {
        InitializeComponent();
    }

    private void ManageProfiles_Click(object sender, RoutedEventArgs e)
    {
        ManageProfilesRequested?.Invoke(this, EventArgs.Empty);
    }
}
