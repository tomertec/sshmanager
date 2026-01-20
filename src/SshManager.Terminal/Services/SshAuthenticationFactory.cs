using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshNet.Agent;
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

            // Kerberos/GSSAPI authentication using Windows domain credentials
            AuthType.Kerberos => CreateKerberosAuth(connectionInfo, kbInteractiveCallback),

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
    /// of the SSH.NET library API design. The library does not support <see cref="System.Security.SecureString"/>
    /// or other memory-protected credential types.</para>
    ///
    /// <para><strong>Memory Security Risk:</strong> The password exists as an unprotected, immutable string
    /// in memory during the authentication process. It cannot be securely zeroed because strings are immutable
    /// in .NET. This creates a window where the credential could be exposed in:</para>
    /// <list type="bullet">
    /// <item>Memory dumps (process crash dumps, debugging sessions)</item>
    /// <item>Pagefile/swap if memory is paged to disk</item>
    /// <item>Hibernation files if the system hibernates</item>
    /// <item>Memory forensics if the machine is compromised</item>
    /// </list>
    ///
    /// <para><strong>Mitigations in Place:</strong></para>
    /// <list type="bullet">
    /// <item>Passwords are stored encrypted at rest using DPAPI via <see cref="Security.DpapiSecretProtector"/></item>
    /// <item>Passwords are decrypted only when needed for authentication (just-in-time)</item>
    /// <item>Passwords are cleared from the <see cref="TerminalConnectionInfo"/> object immediately after use</item>
    /// <item>Gen 2 garbage collection is requested after authentication to encourage memory reclamation</item>
    /// <item>The credential cache uses <see cref="System.Security.SecureString"/> for in-memory storage</item>
    /// <item>Cached credentials have automatic expiration</item>
    /// </list>
    ///
    /// <para><strong>Recommended Best Practices:</strong></para>
    /// <list type="bullet">
    /// <item>Prefer SSH key-based authentication (AuthType.SshAgent or AuthType.PrivateKeyFile) over passwords</item>
    /// <item>Use keyboard-interactive authentication for 2FA/TOTP which minimizes password exposure time</item>
    /// <item>Enable BitLocker/full-disk encryption to protect against physical memory access</item>
    /// <item>Disable hibernation and minimize pagefile usage on sensitive systems</item>
    /// </list>
    ///
    /// <para><strong>Residual Risk:</strong> Despite these mitigations, the brief window during SSH.NET
    /// authentication where the password exists as a plain string cannot be eliminated without changes
    /// to the SSH.NET library itself. This is a fundamental limitation of the current .NET SSH implementation.</para>
    /// </remarks>
    private SshAuthenticationResult CreatePasswordAuth(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        // SECURITY NOTE: SSH.NET requires plain string passwords. See method documentation for details.
        // The password is briefly exposed in memory during this authentication process.
        //
        // MITIGATION: After this method returns, the caller should:
        // 1. Clear the password from the connectionInfo object
        // 2. Request GC collection to reclaim the string memory
        //
        // The password string will still exist in Gen 0/1/2 heap until garbage collection,
        // but requesting collection minimizes the exposure window.

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
        var methods = new List<AuthenticationMethod>();
        var disposables = new List<IDisposable>();

        // 1. Try real SSH agents first (Pageant, then OpenSSH Agent)
        var agentKeys = TryGetAgentKeys();

        if (agentKeys.Count > 0)
        {
            _logger.LogInformation("Using {KeyCount} keys from SSH agent for authentication", agentKeys.Count);
            methods.Add(new PrivateKeyAuthenticationMethod(connectionInfo.Username, agentKeys.ToArray()));
        }
        else
        {
            // 2. Fall back to local key file scanning (backward compatibility)
            _logger.LogDebug("No SSH agent available, falling back to local key files in ~/.ssh/");
            var localKeys = LoadLocalKeyFiles(disposables);

            if (localKeys.Count > 0)
            {
                _logger.LogInformation("Using {KeyCount} local SSH keys for authentication", localKeys.Count);
                methods.Add(new PrivateKeyAuthenticationMethod(connectionInfo.Username, localKeys.ToArray()));
            }
        }

        // 3. Add keyboard-interactive for 2FA or as fallback
        var kbAuth = CreateKeyboardInteractiveAuth(connectionInfo.Username, kbInteractiveCallback);
        if (kbAuth != null)
        {
            methods.Add(kbAuth);
            _logger.LogDebug("Added keyboard-interactive authentication method");
        }

        if (methods.Count == 0)
        {
            _logger.LogInformation("No SSH agent or local keys available, using keyboard-interactive authentication");
            methods.Add(new KeyboardInteractiveAuthenticationMethod(connectionInfo.Username));
        }

        var result = new SshAuthenticationResult { Methods = methods.ToArray() };
        foreach (var d in disposables)
        {
            result.Disposables.Add(d);
        }
        return result;
    }

    /// <summary>
    /// Attempts to get keys from a running SSH agent (Pageant or OpenSSH Agent).
    /// Returns an empty list if no agent is available.
    /// </summary>
    private List<IPrivateKeySource> TryGetAgentKeys()
    {
        // Try Pageant first (most common on Windows)
        try
        {
            var pageant = new Pageant();
            var keys = pageant.RequestIdentities().ToList();
            if (keys.Count > 0)
            {
                _logger.LogDebug("Connected to Pageant SSH agent with {Count} keys", keys.Count);
                return keys.Cast<IPrivateKeySource>().ToList();
            }
            _logger.LogDebug("Pageant is running but has no keys loaded");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Pageant not available: {Message}", ex.Message);
        }

        // Try OpenSSH Agent (Windows named pipe or Unix socket)
        try
        {
            var agent = new SshAgent();
            var keys = agent.RequestIdentities().ToList();
            if (keys.Count > 0)
            {
                _logger.LogDebug("Connected to OpenSSH Agent with {Count} keys", keys.Count);
                return keys.Cast<IPrivateKeySource>().ToList();
            }
            _logger.LogDebug("OpenSSH Agent is running but has no keys loaded");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("OpenSSH Agent not available: {Message}", ex.Message);
        }

        return new List<IPrivateKeySource>();
    }

    /// <summary>
    /// Loads SSH keys from the user's ~/.ssh/ directory.
    /// This is the fallback when no SSH agent is available.
    /// </summary>
    private List<PrivateKeyFile> LoadLocalKeyFiles(List<IDisposable> disposables)
    {
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var keyFiles = new List<PrivateKeyFile>();
        var skippedKeys = new List<(string keyPath, string reason)>();

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
                    _logger.LogDebug("Loaded local SSH key: {KeyPath}", keyPath);
                }
                catch (Exception ex)
                {
                    skippedKeys.Add((keyPath, ex.Message));
                    _logger.LogWarning(ex, "Failed to load SSH key {KeyPath} - it may be encrypted or in an unsupported format", keyPath);
                }
            }
        }

        if (skippedKeys.Count > 0)
        {
            _logger.LogWarning("Skipped {SkippedCount} keys that could not be loaded", skippedKeys.Count);
        }

        return keyFiles;
    }

    /// <summary>
    /// Creates private key-based authentication methods.
    /// </summary>
    /// <remarks>
    /// <para><strong>SECURITY NOTE:</strong> SSH.NET's <see cref="PrivateKeyFile"/> constructor
    /// requires the passphrase as a plain <see cref="string"/>. Like password authentication,
    /// the passphrase briefly exists in memory as an unprotected string during key decryption.</para>
    /// <para>The same memory security considerations apply as with password authentication.
    /// See <see cref="CreatePasswordAuth"/> for details on mitigations and residual risks.</para>
    /// </remarks>
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
                // SECURITY NOTE: SSH.NET requires plain string passphrases for encrypted private keys.
                // The passphrase is exposed in memory during key decryption.
                // Prefer unencrypted keys stored in secure locations with restrictive file permissions,
                // or use SSH agent authentication which keeps keys in the agent's memory space.
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
    /// Creates Kerberos/GSSAPI authentication using Windows domain credentials.
    /// </summary>
    /// <remarks>
    /// <para>This method uses keyboard-interactive authentication which can work with
    /// Kerberos-enabled SSH servers when combined with Windows SSO.</para>
    /// <para>For full GSSAPI authentication, the SSH server must be configured to accept
    /// keyboard-interactive authentication with Kerberos integration.</para>
    /// <para>GSSAPI authentication requirements:</para>
    /// <list type="bullet">
    /// <item>Windows domain membership (Active Directory)</item>
    /// <item>Valid Kerberos TGT (Ticket Granting Ticket)</item>
    /// <item>SSH server configured to accept GSSAPI or keyboard-interactive authentication</item>
    /// </list>
    /// </remarks>
    private SshAuthenticationResult CreateKerberosAuth(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        _logger.LogDebug("Creating Kerberos/GSSAPI authentication for {Username}@{Host}",
            connectionInfo.Username, connectionInfo.Hostname);

        var methods = new List<AuthenticationMethod>();

        // Create keyboard-interactive authentication method
        // Many SSH servers support Kerberos through keyboard-interactive
        // when the Windows client has a valid TGT
        var kbAuth = CreateKeyboardInteractiveAuth(connectionInfo.Username, kbInteractiveCallback);
        if (kbAuth != null)
        {
            methods.Add(kbAuth);
            _logger.LogInformation("Keyboard-interactive authentication method created for Kerberos SSO");
        }
        else
        {
            // If no callback, create a basic keyboard-interactive method
            methods.Add(new KeyboardInteractiveAuthenticationMethod(connectionInfo.Username));
            _logger.LogDebug("Added basic keyboard-interactive authentication for Kerberos");
        }

        // Also try SSH agent as fallback if keys are available
        // This helps when Kerberos fails but user has SSH keys in an agent
        var agentKeys = TryGetAgentKeys();
        if (agentKeys.Count > 0)
        {
            methods.Add(new PrivateKeyAuthenticationMethod(connectionInfo.Username, agentKeys.ToArray()));
            _logger.LogDebug("Added SSH agent keys as fallback for Kerberos authentication");
        }

        return new SshAuthenticationResult { Methods = methods.ToArray() };
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
