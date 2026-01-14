using Renci.SshNet;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Factory interface for creating SSH authentication methods from connection information.
/// </summary>
public interface ISshAuthenticationFactory
{
    /// <summary>
    /// Creates authentication methods based on the connection information.
    /// </summary>
    /// <param name="connectionInfo">Connection information specifying auth type and credentials.</param>
    /// <param name="kbInteractiveCallback">Optional keyboard-interactive callback for 2FA.</param>
    /// <returns>A result containing authentication methods and disposable resources to be tracked.</returns>
    SshAuthenticationResult CreateAuthMethods(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback = null);
}
