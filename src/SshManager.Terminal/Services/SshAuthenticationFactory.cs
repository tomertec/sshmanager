using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Factory implementation for creating SSH authentication methods from connection information.
/// Supports password authentication, private key authentication, and SSH agent authentication.
/// </summary>
public sealed class SshAuthenticationFactory : ISshAuthenticationFactory
{
    private readonly ILogger<SshAuthenticationFactory> _logger;

    public SshAuthenticationFactory(ILogger<SshAuthenticationFactory>? logger = null)
    {
        _logger = logger ?? NullLogger<SshAuthenticationFactory>.Instance;
    }

    /// <inheritdoc />
    public SshAuthenticationResult CreateAuthMethods(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback = null)
    {
        _logger.LogDebug("Creating authentication methods for {AuthType}", connectionInfo.AuthType);

        return connectionInfo.AuthType switch
        {
            AuthType.Password when !string.IsNullOrEmpty(connectionInfo.Password) =>
                CreatePasswordAuth(connectionInfo, kbInteractiveCallback),

            AuthType.PrivateKeyFile when !string.IsNullOrEmpty(connectionInfo.PrivateKeyPath) =>
                CreatePrivateKeyAuth(connectionInfo, kbInteractiveCallback),

            // For SSH Agent, we try keyboard-interactive or fall back to private key files in default location
            AuthType.SshAgent => CreateAgentAuth(connectionInfo, kbInteractiveCallback),

            // Default fallback - log a warning if we expected password auth
            _ when connectionInfo.AuthType == AuthType.Password =>
                LogAndFallback(connectionInfo, kbInteractiveCallback, "Password auth configured but no password provided"),

            _ => CreateAgentAuth(connectionInfo, kbInteractiveCallback)
        };
    }

    private SshAuthenticationResult LogAndFallback(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        string reason)
    {
        _logger.LogWarning("Falling back to agent auth: {Reason}", reason);
        return CreateAgentAuth(connectionInfo, kbInteractiveCallback);
    }

    /// <summary>
    /// Creates password-based authentication methods.
    /// </summary>
    /// <remarks>
    /// <para><strong>SECURITY LIMITATION:</strong> SSH.NET's <see cref="PasswordAuthenticationMethod"/>
    /// requires a plain <see cref="string"/> parameter for the password. This is an unavoidable limitation
    /// of the SSH.NET library API design.</para>
    ///
    /// <para><strong>Memory Security Risk:</strong> The password exists as an unprotected, immutable string
    /// in memory during the authentication process. It cannot be securely zeroed because strings are immutable
    /// in .NET. This creates a window where the credential could be exposed in memory dumps, debugging sessions,
    /// or if memory is swapped to disk.</para>
    ///
    /// <para><strong>Mitigations in Place:</strong></para>
    /// <list type="bullet">
    /// <item>Passwords are stored encrypted at rest using DPAPI via <see cref="Security.DpapiSecretProtector"/></item>
    /// <item>Passwords are decrypted only when needed for authentication</item>
    /// <item>The credential cache uses <see cref="System.Security.SecureString"/> for in-memory storage</item>
    /// <item>Cached credentials have automatic expiration</item>
    /// </list>
    ///
    /// <para><strong>Residual Risk:</strong> Despite these mitigations, the brief window during SSH.NET
    /// authentication where the password exists as a plain string cannot be eliminated without changes
    /// to the SSH.NET library itself.</para>
    /// </remarks>
    private SshAuthenticationResult CreatePasswordAuth(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        // SECURITY NOTE: SSH.NET requires plain string passwords. See method documentation for details.
        // The password is briefly exposed in memory during this authentication process.
        var methods = new List<AuthenticationMethod>
        {
            new PasswordAuthenticationMethod(connectionInfo.Username, connectionInfo.Password!)
        };

        // Add keyboard-interactive for 2FA after password
        var kbAuth = CreateKeyboardInteractiveAuth(connectionInfo.Username, kbInteractiveCallback);
        if (kbAuth != null)
        {
            methods.Add(kbAuth);
        }

        return new SshAuthenticationResult { Methods = methods.ToArray() };
    }

    private SshAuthenticationResult CreateAgentAuth(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        // Try to find default SSH keys
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var keyFiles = new List<PrivateKeyFile>();
        var skippedKeys = new List<(string keyPath, string reason)>();
        var disposables = new List<IDisposable>();

        _logger.LogDebug("Searching for SSH keys in {SshDir}", sshDir);

        var defaultKeys = new[] { "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa" };
        foreach (var keyName in defaultKeys)
        {
            var keyPath = Path.Combine(sshDir, keyName);
            if (File.Exists(keyPath))
            {
                try
                {
                    var keyFile = new PrivateKeyFile(keyPath);
                    keyFiles.Add(keyFile);
                    // Track for disposal when connection closes
                    disposables.Add(keyFile);
                    _logger.LogDebug("Loaded SSH key: {KeyPath}", keyPath);
                }
                catch (Exception ex)
                {
                    skippedKeys.Add((keyPath, ex.Message));
                    _logger.LogWarning(ex, "Failed to load SSH key {KeyPath} - it may be encrypted or in an unsupported format", keyPath);
                }
            }
        }

        var methods = new List<AuthenticationMethod>();

        if (keyFiles.Count > 0)
        {
            _logger.LogInformation("Using {KeyCount} SSH keys for authentication", keyFiles.Count);
            if (skippedKeys.Count > 0)
            {
                _logger.LogWarning("Skipped {SkippedCount} keys that could not be loaded", skippedKeys.Count);
            }
            methods.Add(new PrivateKeyAuthenticationMethod(connectionInfo.Username, keyFiles.ToArray()));
        }

        // Add keyboard-interactive for 2FA or as fallback
        var kbAuth = CreateKeyboardInteractiveAuth(connectionInfo.Username, kbInteractiveCallback);
        if (kbAuth != null)
        {
            methods.Add(kbAuth);
            _logger.LogDebug("Added keyboard-interactive authentication method");
        }

        if (methods.Count == 0)
        {
            _logger.LogInformation("No usable SSH keys found, falling back to basic keyboard-interactive authentication");
            methods.Add(new KeyboardInteractiveAuthenticationMethod(connectionInfo.Username));
        }

        var result = new SshAuthenticationResult { Methods = methods.ToArray() };
        foreach (var d in disposables)
        {
            result.Disposables.Add(d);
        }
        return result;
    }

    private SshAuthenticationResult CreatePrivateKeyAuth(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        _logger.LogDebug("Loading private key from {KeyPath}", connectionInfo.PrivateKeyPath);

        try
        {
            PrivateKeyFile keyFile;

            if (!string.IsNullOrEmpty(connectionInfo.PrivateKeyPassphrase))
            {
                keyFile = new PrivateKeyFile(connectionInfo.PrivateKeyPath!, connectionInfo.PrivateKeyPassphrase);
                _logger.LogDebug("Private key loaded with passphrase");
            }
            else
            {
                keyFile = new PrivateKeyFile(connectionInfo.PrivateKeyPath!);
                _logger.LogDebug("Private key loaded without passphrase");
            }

            var methods = new List<AuthenticationMethod>
            {
                new PrivateKeyAuthenticationMethod(connectionInfo.Username, keyFile)
            };

            // Add keyboard-interactive for 2FA after key auth
            var kbAuth = CreateKeyboardInteractiveAuth(connectionInfo.Username, kbInteractiveCallback);
            if (kbAuth != null)
            {
                methods.Add(kbAuth);
            }

            var result = new SshAuthenticationResult { Methods = methods.ToArray() };
            // Track key file for disposal when connection closes
            result.Disposables.Add(keyFile);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load private key from {KeyPath}", connectionInfo.PrivateKeyPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a keyboard-interactive authentication method with the callback wired up.
    /// </summary>
    private KeyboardInteractiveAuthenticationMethod? CreateKeyboardInteractiveAuth(
        string username,
        KeyboardInteractiveCallback? callback)
    {
        if (callback == null)
        {
            return null;
        }

        var kbAuth = new KeyboardInteractiveAuthenticationMethod(username);

        kbAuth.AuthenticationPrompt += (sender, e) =>
        {
            try
            {
                var sshPrompts = e.Prompts;
                _logger.LogDebug("Received keyboard-interactive prompt for {Username} with {PromptCount} prompts",
                    e.Username, sshPrompts.Count);

                // Convert SSH.NET prompts to our model
                var prompts = sshPrompts.Select(p => new AuthenticationPrompt
                {
                    Prompt = p.Request,
                    IsPassword = p.IsEchoed == false // SSH.NET: IsEchoed=false means password
                }).ToList();

                var request = new AuthenticationRequest
                {
                    Name = e.Username ?? "",
                    Instruction = e.Instruction ?? "",
                    Prompts = prompts
                };

                // NOTE: SSH.NET's AuthenticationPrompt event is synchronous and does not support async handlers.
                // This event fires on SSH.NET's internal background thread during authentication,
                // not on the UI thread, so blocking here does not cause UI thread deadlocks.
                // The blocking call is unavoidable due to the SSH.NET library's synchronous event design.
                // ConfigureAwait(false) ensures we don't try to marshal back to any captured context.
                var responseTask = callback(request);
                var response = responseTask.ConfigureAwait(false).GetAwaiter().GetResult();

                if (response != null)
                {
                    // Apply responses back to SSH.NET prompts
                    for (int i = 0; i < sshPrompts.Count && i < response.Prompts.Count; i++)
                    {
                        sshPrompts[i].Response = response.Prompts[i].Response ?? "";
                        _logger.LogDebug("Set response for prompt {Index}: {PromptText}",
                            i, sshPrompts[i].Request);
                    }
                }
                else
                {
                    _logger.LogWarning("Keyboard-interactive authentication cancelled by user");
                    // Set empty responses to fail auth gracefully
                    foreach (var prompt in sshPrompts)
                    {
                        prompt.Response = "";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during keyboard-interactive authentication");
                // Set empty responses on error
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = "";
                }
            }
        };

        return kbAuth;
    }
}
