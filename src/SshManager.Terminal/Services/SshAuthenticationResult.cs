using Renci.SshNet;

namespace SshManager.Terminal.Services;

/// <summary>
/// Result of creating SSH authentication methods.
/// Contains both the authentication methods and disposable resources that need cleanup.
/// </summary>
public sealed class SshAuthenticationResult
{
    /// <summary>
    /// Gets or sets the authentication methods to be used for SSH connection.
    /// </summary>
    public required AuthenticationMethod[] Methods { get; init; }

    /// <summary>
    /// Gets the list of disposable resources (e.g., PrivateKeyFile instances) that need cleanup.
    /// These should be disposed when the connection is closed or if the connection fails.
    /// </summary>
    public List<IDisposable> Disposables { get; } = new();
}
