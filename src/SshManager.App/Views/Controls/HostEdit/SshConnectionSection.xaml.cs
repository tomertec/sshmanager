using System.Windows;
using System.Windows.Controls;

namespace SshManager.App.Views.Controls.HostEdit;

/// <summary>
/// SSH connection settings section: hostname, port, username, and host profile.
/// </summary>
public partial class SshConnectionSection : UserControl
{
    /// <summary>
    /// Event raised when the user wants to manage host profiles.
    /// </summary>
    public event EventHandler? ManageHostProfilesRequested;

    public SshConnectionSection()
    {
        InitializeComponent();
    }

    private void ManageHostProfiles_Click(object sender, RoutedEventArgs e)
    {
        ManageHostProfilesRequested?.Invoke(this, EventArgs.Empty);
    }
}
