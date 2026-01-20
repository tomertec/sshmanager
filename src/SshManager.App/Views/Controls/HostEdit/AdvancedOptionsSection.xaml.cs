using System.Windows;
using System.Windows.Controls;

namespace SshManager.App.Views.Controls.HostEdit;

/// <summary>
/// Advanced options section: ProxyJump profile, port forwarding, shell type,
/// keep-alive settings, X11 forwarding, and environment variables.
/// </summary>
public partial class AdvancedOptionsSection : UserControl
{
    /// <summary>
    /// Event raised when the user wants to manage ProxyJump profiles.
    /// </summary>
    public event EventHandler? ManageProxyJumpProfilesRequested;

    /// <summary>
    /// Event raised when the user wants to manage port forwarding.
    /// </summary>
    public event EventHandler? ManagePortForwardingRequested;

    public AdvancedOptionsSection()
    {
        InitializeComponent();
    }

    private void ManageProxyJumpProfiles_Click(object sender, RoutedEventArgs e)
    {
        ManageProxyJumpProfilesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ManagePortForwarding_Click(object sender, RoutedEventArgs e)
    {
        ManagePortForwardingRequested?.Invoke(this, EventArgs.Empty);
    }
}
