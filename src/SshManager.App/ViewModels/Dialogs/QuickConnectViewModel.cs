using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Quick Connect dialog.
/// Allows connecting to a host by entering just an IP/hostname,
/// or providing full credentials for immediate authentication.
/// </summary>
public partial class QuickConnectViewModel : ObservableObject
{
    /// <summary>
    /// Event raised when the dialog should close.
    /// </summary>
    public event Action? RequestClose;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _hostname = "";

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    /// <summary>
    /// Gets or sets the dialog result.
    /// </summary>
    public bool? DialogResult { get; private set; }

    /// <summary>
    /// Gets the created host entry after successful connection request.
    /// This is a temporary (non-persisted) host entry used for the connection.
    /// </summary>
    public HostEntry? CreatedHostEntry { get; private set; }

    /// <summary>
    /// Gets whether the user provided credentials (username and/or password).
    /// If false, the connection will use SSH Agent authentication.
    /// </summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// Gets whether only hostname was provided (no credentials).
    /// In this case, the SSH connection will prompt for credentials interactively.
    /// </summary>
    public bool HostnameOnly => !HasCredentials;

    private bool CanConnect => !string.IsNullOrWhiteSpace(Hostname);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        if (!CanConnect) return;

        // Parse hostname:port format if user entered it that way
        var hostname = Hostname.Trim();
        var port = Port;

        // Check for host:port format
        var colonIndex = hostname.LastIndexOf(':');
        if (colonIndex > 0)
        {
            var portPart = hostname[(colonIndex + 1)..];
            if (int.TryParse(portPart, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                hostname = hostname[..colonIndex];
                port = parsedPort;
            }
        }

        // Determine auth type based on provided credentials
        AuthType authType;
        if (!string.IsNullOrWhiteSpace(Password))
        {
            authType = AuthType.Password;
        }
        else
        {
            // Use SSH Agent for key-based auth or keyboard-interactive
            authType = AuthType.SshAgent;
        }

        // Create a temporary host entry for the connection
        CreatedHostEntry = new HostEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = string.IsNullOrWhiteSpace(Username)
                ? hostname
                : $"{Username}@{hostname}",
            Hostname = hostname,
            Port = port,
            Username = string.IsNullOrWhiteSpace(Username) ? Environment.UserName : Username.Trim(),
            AuthType = authType,
            // Note: Password is not stored in PasswordProtected since this is temporary
            // It will be passed separately for the connection
            Notes = "Quick Connect (temporary)"
        };

        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }
}
