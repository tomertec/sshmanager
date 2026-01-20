using System.Windows;
using System.Windows.Controls;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Toolbar control for terminal sessions containing broadcast mode, session logging,
/// SFTP browser, tunnel builder, and port forwarding controls.
/// </summary>
public partial class TerminalToolbar : UserControl
{
    /// <summary>
    /// Event raised when the tunnel builder button is clicked.
    /// </summary>
    public event EventHandler? TunnelBuilderRequested;

    /// <summary>
    /// Event raised when the port forwarding "Manage Profiles" button is clicked.
    /// </summary>
    public event EventHandler? ManagePortForwardingProfilesRequested;

    public TerminalToolbar()
    {
        InitializeComponent();
    }

    private void TunnelBuilderButton_Click(object sender, RoutedEventArgs e)
    {
        TunnelBuilderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PortForwardingPanel_ManageProfilesRequested(object? sender, EventArgs e)
    {
        ManagePortForwardingProfilesRequested?.Invoke(this, EventArgs.Empty);
    }
}
