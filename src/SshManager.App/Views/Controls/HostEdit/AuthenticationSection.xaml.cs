using System.Windows;
using System.Windows.Controls;

namespace SshManager.App.Views.Controls.HostEdit;

/// <summary>
/// Authentication settings section: auth type selector and all auth method-specific UI.
/// Handles SSH Agent, Private Key File, Password, Kerberos, and 1Password authentication.
/// </summary>
public partial class AuthenticationSection : UserControl
{
    /// <summary>
    /// Event raised when the user wants to select from existing SSH keys.
    /// </summary>
    public event EventHandler? SelectKeyRequested;

    /// <summary>
    /// Event raised when the password changes (PasswordBox cannot use data binding).
    /// </summary>
    public event EventHandler<string>? PasswordChanged;

    /// <summary>
    /// Event raised when the user wants to browse 1Password for a secret reference.
    /// The string parameter indicates which field to populate: "password" or "key".
    /// </summary>
    public event EventHandler<string>? BrowseOnePasswordRequested;

    public AuthenticationSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the password in the PasswordBox (call this from parent after loading).
    /// </summary>
    public void SetPassword(string? password)
    {
        if (!string.IsNullOrEmpty(password))
        {
            PasswordBox.Password = password;
        }
    }

    private void SelectKeyButton_Click(object sender, RoutedEventArgs e)
    {
        SelectKeyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            PasswordChanged?.Invoke(this, passwordBox.Password);
        }
    }

    private void BrowseOnePasswordPassword_Click(object sender, RoutedEventArgs e)
    {
        BrowseOnePasswordRequested?.Invoke(this, "password");
    }

    private void BrowseOnePasswordKey_Click(object sender, RoutedEventArgs e)
    {
        BrowseOnePasswordRequested?.Invoke(this, "key");
    }
}
