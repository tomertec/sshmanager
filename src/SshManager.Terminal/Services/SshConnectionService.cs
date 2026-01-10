using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for establishing SSH connections using SSH.NET.
/// </summary>
public sealed class SshConnectionService : ISshConnectionService
{
    private const int DefaultBufferSize = 4096;
    private const string DefaultTerminalName = "xterm-256color";
    private const int MinPort = 1;
    private const int MaxPort = 65535;

    private readonly ILogger<SshConnectionService> _logger;
    private readonly ITerminalResizeService _resizeService;

    public SshConnectionService(
        ILogger<SshConnectionService>? logger = null,
        ITerminalResizeService? resizeService = null)
    {
        _logger = logger ?? NullLogger<SshConnectionService>.Instance;
        _resizeService = resizeService ?? new TerminalResizeService();
    }

    public Task<ISshConnection> ConnectAsync(
        TerminalConnectionInfo connectionInfo,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default)
    {
        return ConnectAsync(connectionInfo, null, null, columns, rows, ct);
    }

    public Task<ISshConnection> ConnectAsync(
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default)
    {
        return ConnectAsync(connectionInfo, hostKeyCallback, null, columns, rows, ct);
    }

    public async Task<ISshConnection> ConnectAsync(
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default)
    {
        // Validate input parameters
        ArgumentNullException.ThrowIfNull(connectionInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionInfo.Hostname, nameof(connectionInfo.Hostname));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionInfo.Username, nameof(connectionInfo.Username));

        if (connectionInfo.Port < MinPort || connectionInfo.Port > MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionInfo.Port),
                connectionInfo.Port,
                $"Port must be between {MinPort} and {MaxPort}.");
        }

        _logger.LogInformation("Connecting to {Host}:{Port} as {Username} using {AuthType}",
            connectionInfo.Hostname, connectionInfo.Port, connectionInfo.Username, connectionInfo.AuthType);

        var authResult = CreateAuthMethods(connectionInfo, kbInteractiveCallback);
        var connInfo = new ConnectionInfo(
            connectionInfo.Hostname,
            connectionInfo.Port,
            connectionInfo.Username,
            authResult.Methods)
        {
            Timeout = connectionInfo.Timeout
        };

        // Configure key exchange algorithms to support more servers
        ConfigureAlgorithms(connInfo);

        var client = new SshClient(connInfo);

        if (connectionInfo.KeepAliveInterval.HasValue &&
            connectionInfo.KeepAliveInterval.Value > TimeSpan.Zero)
        {
            client.KeepAliveInterval = connectionInfo.KeepAliveInterval.Value;
        }
        var hostKeyVerificationResult = true;
        Exception? hostKeyException = null;

        // Set up host key verification if callback provided
        if (hostKeyCallback != null)
        {
            client.HostKeyReceived += (sender, e) =>
            {
                try
                {
                    var fingerprint = ComputeFingerprint(e.HostKey);
                    _logger.LogDebug("Received host key: {Algorithm} {Fingerprint}", e.HostKeyName, fingerprint);

                    // NOTE: SSH.NET's HostKeyReceived event is synchronous and does not support async handlers.
                    // This event fires on SSH.NET's internal background thread during the connection handshake,
                    // not on the UI thread, so blocking here does not cause UI thread deadlocks.
                    // The blocking call is unavoidable due to the SSH.NET library's synchronous event design.
                    // ConfigureAwait(false) ensures we don't try to marshal back to any captured context.
                    var verifyTask = hostKeyCallback(
                        connectionInfo.Hostname,
                        connectionInfo.Port,
                        e.HostKeyName,
                        fingerprint,
                        e.HostKey);

                    hostKeyVerificationResult = verifyTask.ConfigureAwait(false).GetAwaiter().GetResult();
                    e.CanTrust = hostKeyVerificationResult;

                    if (!hostKeyVerificationResult)
                    {
                        _logger.LogWarning("Host key rejected by user for {Host}:{Port}", connectionInfo.Hostname, connectionInfo.Port);
                    }
                }
                catch (Exception ex)
                {
                    hostKeyException = ex;
                    e.CanTrust = false;
                    _logger.LogError(ex, "Error during host key verification");
                }
            };
        }

        try
        {
            // Connect on background thread to avoid blocking UI
            await Task.Run(() => client.Connect(), ct);

            if (hostKeyException != null)
            {
                throw hostKeyException;
            }

            if (!hostKeyVerificationResult)
            {
                client.Dispose();
                DisposeAuthResources(authResult);
                throw new InvalidOperationException("Connection rejected: Host key verification failed.");
            }

            _logger.LogInformation("SSH connection established to {Host}:{Port}", connectionInfo.Hostname, connectionInfo.Port);
        }
        catch (Exception ex)
        {
            // Log detailed algorithm info on failure to help diagnose negotiation issues
            _logger.LogError("Connection to {Host}:{Port} failed: {Message}",
                connectionInfo.Hostname, connectionInfo.Port, ex.Message);
            _logger.LogError("Offered KEX algorithms: {Algorithms}",
                string.Join(", ", connInfo.KeyExchangeAlgorithms.Keys));
            _logger.LogError("Offered host key algorithms: {Algorithms}",
                string.Join(", ", connInfo.HostKeyAlgorithms.Keys));
            _logger.LogError("Offered encryptions: {Algorithms}",
                string.Join(", ", connInfo.Encryptions.Keys));
            _logger.LogError(ex, "Full exception details");
            client.Dispose();
            // Dispose auth resources (PrivateKeyFile instances) on connection failure
            DisposeAuthResources(authResult);
            throw;
        }

        // Create shell stream with terminal settings
        var shellStream = client.CreateShellStream(
            terminalName: DefaultTerminalName,
            columns: columns,
            rows: rows,
            width: 0,
            height: 0,
            bufferSize: DefaultBufferSize);

        _logger.LogDebug("Shell stream created with {Columns}x{Rows} terminal", columns, rows);

        var connection = new SshConnection(client, shellStream, _logger, _resizeService);

        // Transfer ownership of disposable resources to the connection
        foreach (var disposable in authResult.Disposables)
        {
            connection.TrackDisposable(disposable);
        }

        return connection;
    }

    /// <inheritdoc />
    public async Task<ISshConnection> ConnectWithProxyChainAsync(
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns = 80,
        uint rows = 24,
        CancellationToken ct = default)
    {
        if (connectionChain.Count == 0)
        {
            throw new ArgumentException("Connection chain cannot be empty.", nameof(connectionChain));
        }

        // If only one hop, use direct connection
        if (connectionChain.Count == 1)
        {
            return await ConnectAsync(
                connectionChain[0],
                hostKeyCallback,
                kbInteractiveCallback,
                columns,
                rows,
                ct);
        }

        _logger.LogInformation("Connecting through proxy chain with {HopCount} hops",
            connectionChain.Count);

        // Track all intermediate connections and resources for cleanup
        var intermediateClients = new List<SshClient>();
        var forwardedPorts = new List<ForwardedPortLocal>();
        var disposables = new List<IDisposable>();

        try
        {
            SshClient? currentClient = null;
            int currentLocalPort = 0;

            // Connect through each hop except the last
            for (int i = 0; i < connectionChain.Count - 1; i++)
            {
                var hopInfo = connectionChain[i];
                var nextHopInfo = connectionChain[i + 1];

                _logger.LogDebug("Connecting to hop {Index}: {Host}:{Port}",
                    i + 1, hopInfo.Hostname, hopInfo.Port);

                // Create connection info and auth methods
                var authResult = CreateAuthMethods(hopInfo, kbInteractiveCallback);
                disposables.AddRange(authResult.Disposables);

                ConnectionInfo connInfo;
                if (i == 0)
                {
                    // First hop - connect directly
                    connInfo = new ConnectionInfo(
                        hopInfo.Hostname,
                        hopInfo.Port,
                        hopInfo.Username,
                        authResult.Methods)
                    {
                        Timeout = hopInfo.Timeout
                    };
                }
                else
                {
                    // Subsequent hops - connect through local forward from previous hop
                    connInfo = new ConnectionInfo(
                        "127.0.0.1",
                        currentLocalPort,
                        hopInfo.Username,
                        authResult.Methods)
                    {
                        Timeout = hopInfo.Timeout
                    };
                }

                ConfigureAlgorithms(connInfo);

                var client = new SshClient(connInfo);
                if (hopInfo.KeepAliveInterval.HasValue && hopInfo.KeepAliveInterval.Value > TimeSpan.Zero)
                {
                    client.KeepAliveInterval = hopInfo.KeepAliveInterval.Value;
                }

                // Set up host key verification
                SetupHostKeyVerification(client, hopInfo, hostKeyCallback);

                // Connect
                await Task.Run(() => client.Connect(), ct);
                _logger.LogInformation("Connected to hop {Index}: {Host}",
                    i + 1, hopInfo.Hostname);

                intermediateClients.Add(client);
                currentClient = client;

                // Set up local port forward to the next hop
                currentLocalPort = FindAvailablePort();
                var forwardedPort = new ForwardedPortLocal(
                    "127.0.0.1",
                    (uint)currentLocalPort,
                    nextHopInfo.Hostname,
                    (uint)nextHopInfo.Port);

                client.AddForwardedPort(forwardedPort);
                forwardedPort.Start();
                forwardedPorts.Add(forwardedPort);

                _logger.LogDebug("Created local forward on port {LocalPort} to {NextHost}:{NextPort}",
                    currentLocalPort, nextHopInfo.Hostname, nextHopInfo.Port);
            }

            // Now connect to the final target through the last forward
            var targetInfo = connectionChain[^1];
            _logger.LogDebug("Connecting to final target: {Host}:{Port}",
                targetInfo.Hostname, targetInfo.Port);

            var targetAuthResult = CreateAuthMethods(targetInfo, kbInteractiveCallback);
            disposables.AddRange(targetAuthResult.Disposables);

            var targetConnInfo = new ConnectionInfo(
                "127.0.0.1",
                currentLocalPort,
                targetInfo.Username,
                targetAuthResult.Methods)
            {
                Timeout = targetInfo.Timeout
            };

            ConfigureAlgorithms(targetConnInfo);

            var targetClient = new SshClient(targetConnInfo);
            if (targetInfo.KeepAliveInterval.HasValue && targetInfo.KeepAliveInterval.Value > TimeSpan.Zero)
            {
                targetClient.KeepAliveInterval = targetInfo.KeepAliveInterval.Value;
            }

            // Set up host key verification for target
            SetupHostKeyVerification(targetClient, targetInfo, hostKeyCallback);

            // Connect to target
            await Task.Run(() => targetClient.Connect(), ct);
            _logger.LogInformation("Connected to final target: {Host}", targetInfo.Hostname);

            // Create shell stream on target
            var shellStream = targetClient.CreateShellStream(
                terminalName: DefaultTerminalName,
                columns: columns,
                rows: rows,
                width: 0,
                height: 0,
                bufferSize: DefaultBufferSize);

            // Create the chained connection wrapper
            var connection = new ProxyChainSshConnection(
                targetClient,
                shellStream,
                intermediateClients,
                forwardedPorts,
                _logger,
                _resizeService);

            // Track auth resources for cleanup
            foreach (var disposable in disposables)
            {
                connection.TrackDisposable(disposable);
            }

            _logger.LogInformation("Proxy chain connection established: {Chain}",
                string.Join(" → ", connectionChain.Select(c => c.Hostname)));

            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish proxy chain connection");

            // Clean up on failure
            foreach (var port in forwardedPorts)
            {
                try
                {
                    port.Stop();
                    port.Dispose();
                }
                catch { }
            }

            foreach (var client in intermediateClients)
            {
                try
                {
                    client.Disconnect();
                    client.Dispose();
                }
                catch { }
            }

            foreach (var d in disposables)
            {
                try { d.Dispose(); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Sets up host key verification for a client.
    /// </summary>
    private void SetupHostKeyVerification(
        SshClient client,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback)
    {
        if (hostKeyCallback == null) return;

        client.HostKeyReceived += (sender, e) =>
        {
            try
            {
                var fingerprint = ComputeFingerprint(e.HostKey);
                _logger.LogDebug("Received host key for {Host}: {Algorithm} {Fingerprint}",
                    connectionInfo.Hostname, e.HostKeyName, fingerprint);

                // NOTE: SSH.NET's HostKeyReceived event is synchronous and does not support async handlers.
                // This event fires on SSH.NET's internal background thread during the connection handshake,
                // not on the UI thread, so blocking here does not cause UI thread deadlocks.
                var verifyTask = hostKeyCallback(
                    connectionInfo.Hostname,
                    connectionInfo.Port,
                    e.HostKeyName,
                    fingerprint,
                    e.HostKey);

                e.CanTrust = verifyTask.ConfigureAwait(false).GetAwaiter().GetResult();

                if (!e.CanTrust)
                {
                    _logger.LogWarning("Host key rejected for {Host}:{Port}",
                        connectionInfo.Hostname, connectionInfo.Port);
                }
            }
            catch (Exception ex)
            {
                e.CanTrust = false;
                _logger.LogError(ex, "Error during host key verification for {Host}",
                    connectionInfo.Hostname);
            }
        };
    }

    /// <summary>
    /// Finds an available local port for port forwarding.
    /// </summary>
    private static int FindAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Disposes authentication resources when connection fails.
    /// </summary>
    private void DisposeAuthResources(AuthMethodsResult authResult)
    {
        foreach (var disposable in authResult.Disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing auth resource on connection failure");
            }
        }
        authResult.Disposables.Clear();
    }

    /// <summary>
    /// Computes the SHA256 fingerprint of a host key in base64 format.
    /// </summary>
    public static string ComputeFingerprint(byte[] hostKey)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(hostKey);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    /// <summary>
    /// Configures key exchange, encryption, and MAC algorithms to maximize server compatibility.
    /// SSH.NET may not offer all algorithms by default, especially newer ones.
    /// </summary>
    private void ConfigureAlgorithms(ConnectionInfo connInfo)
    {
        // Log the available algorithms for debugging
        _logger.LogDebug("Available key exchange algorithms: {Algorithms}",
            string.Join(", ", connInfo.KeyExchangeAlgorithms.Keys));
        _logger.LogDebug("Available encryption algorithms: {Algorithms}",
            string.Join(", ", connInfo.Encryptions.Keys));
        _logger.LogDebug("Available host key algorithms: {Algorithms}",
            string.Join(", ", connInfo.HostKeyAlgorithms.Keys));
        _logger.LogDebug("Available HMAC algorithms: {Algorithms}",
            string.Join(", ", connInfo.HmacAlgorithms.Keys));

        // SSH.NET 2024.x should support modern algorithms, but some servers require
        // specific algorithm ordering or may reject certain older algorithms.
        // We reorder to prioritize modern, secure algorithms.

        // Reorder key exchange algorithms to prioritize modern ones
        var preferredKex = new[]
        {
            "curve25519-sha256",
            "curve25519-sha256@libssh.org",
            "ecdh-sha2-nistp521",
            "ecdh-sha2-nistp384",
            "ecdh-sha2-nistp256",
            "diffie-hellman-group-exchange-sha256",
            "diffie-hellman-group16-sha512",
            "diffie-hellman-group14-sha256",
            "diffie-hellman-group14-sha1",
            "diffie-hellman-group-exchange-sha1",
            "diffie-hellman-group1-sha1"
        };

        ReorderAlgorithms(connInfo.KeyExchangeAlgorithms, preferredKex);
    }

    /// <summary>
    /// Reorders algorithms in the dictionary to match preferred order.
    /// Algorithms not in the preferred list are kept at the end.
    /// </summary>
    private static void ReorderAlgorithms<T>(IDictionary<string, T> algorithms, string[] preferredOrder)
    {
        // Create a copy of current algorithms
        var currentAlgorithms = algorithms.ToList();

        // Clear and re-add in preferred order
        algorithms.Clear();

        // First add algorithms in preferred order
        foreach (var name in preferredOrder)
        {
            var match = currentAlgorithms.FirstOrDefault(a =>
                a.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null)
            {
                algorithms[match.Key] = match.Value;
                currentAlgorithms.Remove(match);
            }
        }

        // Then add remaining algorithms that weren't in the preferred list
        foreach (var kvp in currentAlgorithms)
        {
            algorithms[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Result of creating authentication methods, including disposable resources that need cleanup.
    /// </summary>
    private sealed class AuthMethodsResult
    {
        public required AuthenticationMethod[] Methods { get; init; }
        public List<IDisposable> Disposables { get; } = new();
    }

    private AuthMethodsResult CreateAuthMethods(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
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

    private AuthMethodsResult LogAndFallback(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        string reason)
    {
        _logger.LogWarning("Falling back to agent auth: {Reason}", reason);
        return CreateAgentAuth(connectionInfo, kbInteractiveCallback);
    }

    private AuthMethodsResult CreatePasswordAuth(
        TerminalConnectionInfo connectionInfo,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
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

        return new AuthMethodsResult { Methods = methods.ToArray() };
    }

    private AuthMethodsResult CreateAgentAuth(
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

        var result = new AuthMethodsResult { Methods = methods.ToArray() };
        foreach (var d in disposables)
        {
            result.Disposables.Add(d);
        }
        return result;
    }

    private AuthMethodsResult CreatePrivateKeyAuth(
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

            var result = new AuthMethodsResult { Methods = methods.ToArray() };
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

/// <summary>
/// Wraps an SSH client and shell stream as an ISshConnection.
/// </summary>
internal sealed class SshConnection : ISshConnection
{
    private readonly SshClient _client;
    private readonly ILogger _logger;
    private readonly ITerminalResizeService _resizeService;
    private readonly List<IDisposable> _disposables = new();
    private bool _disposed;

    public ShellStream ShellStream { get; }
    public bool IsConnected => _client.IsConnected && !_disposed;
    public event EventHandler? Disconnected;

    public SshConnection(
        SshClient client,
        ShellStream shellStream,
        ILogger logger,
        ITerminalResizeService resizeService)
    {
        _client = client;
        ShellStream = shellStream;
        _logger = logger;
        _resizeService = resizeService;

        // Subscribe to error/disconnect events
        _client.ErrorOccurred += OnError;
        ShellStream.Closed += OnStreamClosed;
    }

    /// <summary>
    /// Registers a disposable resource to be disposed when this connection is closed.
    /// Used to track PrivateKeyFile instances that need cleanup.
    /// </summary>
    public void TrackDisposable(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    public bool ResizeTerminal(uint columns, uint rows)
    {
        return _resizeService.TryResize(ShellStream, columns, rows);
    }

    private void OnError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
    {
        _logger.LogWarning(e.Exception, "SSH connection error occurred");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnStreamClosed(object? sender, EventArgs e)
    {
        _logger.LogInformation("SSH shell stream closed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> RunCommandAsync(string command, TimeSpan? timeout = null)
    {
        if (_disposed || !_client.IsConnected)
        {
            return null;
        }

        try
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);

            return await Task.Run(() =>
            {
                using var cmd = _client.CreateCommand(command);
                cmd.CommandTimeout = actualTimeout;
                var result = cmd.Execute();
                return result?.Trim();
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run command: {Command}", command);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing SSH connection");

        _client.ErrorOccurred -= OnError;
        ShellStream.Closed -= OnStreamClosed;

        try
        {
            ShellStream.Dispose();
            _logger.LogDebug("Shell stream disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing shell stream");
        }

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
                _logger.LogDebug("SSH client disconnected");
            }
            _client.Dispose();
            _logger.LogDebug("SSH client disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing SSH client");
        }

        // Dispose tracked resources (PrivateKeyFile instances)
        var disposableCount = _disposables.Count;
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing tracked resource");
            }
        }
        _disposables.Clear();
        if (disposableCount > 0)
        {
            _logger.LogDebug("Tracked disposables disposed ({Count} items)", disposableCount);
        }

        _logger.LogInformation("SSH connection disposed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(Dispose);
    }
}

/// <summary>
/// Wraps a proxy chain SSH connection that manages multiple chained clients and port forwards.
/// Disposing this connection cleans up all intermediate connections.
/// </summary>
internal sealed class ProxyChainSshConnection : ISshConnection
{
    private readonly SshClient _targetClient;
    private readonly IReadOnlyList<SshClient> _intermediateClients;
    private readonly IReadOnlyList<ForwardedPortLocal> _forwardedPorts;
    private readonly ILogger _logger;
    private readonly ITerminalResizeService _resizeService;
    private readonly List<IDisposable> _disposables = new();
    private bool _disposed;

    public ShellStream ShellStream { get; }
    public bool IsConnected => _targetClient.IsConnected && !_disposed;
    public event EventHandler? Disconnected;

    public ProxyChainSshConnection(
        SshClient targetClient,
        ShellStream shellStream,
        IReadOnlyList<SshClient> intermediateClients,
        IReadOnlyList<ForwardedPortLocal> forwardedPorts,
        ILogger logger,
        ITerminalResizeService resizeService)
    {
        _targetClient = targetClient;
        ShellStream = shellStream;
        _intermediateClients = intermediateClients;
        _forwardedPorts = forwardedPorts;
        _logger = logger;
        _resizeService = resizeService;

        // Subscribe to error/disconnect events
        _targetClient.ErrorOccurred += OnError;
        ShellStream.Closed += OnStreamClosed;

        // Monitor intermediate connections for failures
        foreach (var client in intermediateClients)
        {
            client.ErrorOccurred += OnIntermediateError;
        }
    }

    /// <summary>
    /// Registers a disposable resource to be disposed when this connection is closed.
    /// </summary>
    public void TrackDisposable(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    public bool ResizeTerminal(uint columns, uint rows)
    {
        return _resizeService.TryResize(ShellStream, columns, rows);
    }

    private void OnError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
    {
        _logger.LogWarning(e.Exception, "Proxy chain target connection error occurred");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnIntermediateError(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
    {
        _logger.LogWarning(e.Exception, "Proxy chain intermediate connection error occurred");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnStreamClosed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Proxy chain shell stream closed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> RunCommandAsync(string command, TimeSpan? timeout = null)
    {
        if (_disposed || !_targetClient.IsConnected)
        {
            return null;
        }

        try
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);

            return await Task.Run(() =>
            {
                using var cmd = _targetClient.CreateCommand(command);
                cmd.CommandTimeout = actualTimeout;
                var result = cmd.Execute();
                return result?.Trim();
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run command on proxy chain: {Command}", command);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing proxy chain SSH connection ({HopCount} intermediate hops)",
            _intermediateClients.Count);

        // Unsubscribe from events
        _targetClient.ErrorOccurred -= OnError;
        ShellStream.Closed -= OnStreamClosed;

        foreach (var client in _intermediateClients)
        {
            client.ErrorOccurred -= OnIntermediateError;
        }

        // Dispose shell stream first
        try
        {
            ShellStream.Dispose();
            _logger.LogDebug("Proxy chain shell stream disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing proxy chain shell stream");
        }

        // Dispose target client
        try
        {
            if (_targetClient.IsConnected)
            {
                _targetClient.Disconnect();
            }
            _targetClient.Dispose();
            _logger.LogDebug("Proxy chain target client disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing proxy chain target client");
        }

        // Stop and dispose forwarded ports (in reverse order)
        for (int i = _forwardedPorts.Count - 1; i >= 0; i--)
        {
            try
            {
                _forwardedPorts[i].Stop();
                _forwardedPorts[i].Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing forwarded port {Index}", i);
            }
        }
        _logger.LogDebug("Proxy chain forwarded ports disposed");

        // Dispose intermediate clients (in reverse order - target to first hop)
        for (int i = _intermediateClients.Count - 1; i >= 0; i--)
        {
            try
            {
                if (_intermediateClients[i].IsConnected)
                {
                    _intermediateClients[i].Disconnect();
                }
                _intermediateClients[i].Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing intermediate client {Index}", i);
            }
        }
        _logger.LogDebug("Proxy chain intermediate clients disposed");

        // Dispose tracked resources (PrivateKeyFile instances)
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing tracked resource");
            }
        }
        _disposables.Clear();

        _logger.LogInformation("Proxy chain SSH connection disposed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(Dispose);
    }
}
