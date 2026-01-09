using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for establishing SSH connections using SSH.NET.
/// </summary>
/// <remarks>
/// <para>
/// This service handles the complexity of SSH connection establishment including:
/// - Multiple authentication methods (SSH Agent, Private Key, Password)
/// - Host key verification with fingerprint validation
/// - Keyboard-interactive authentication (2FA/TOTP)
/// - Multi-hop ProxyJump connections through bastion hosts
/// </para>
/// <para>
/// <b>Threading:</b> All public methods are async-safe. The connection itself
/// runs on a background thread to avoid blocking the UI. Host key verification
/// callbacks run on SSH.NET's internal thread, not the UI thread.
/// </para>
/// <para>
/// <b>Resource Management:</b> Returns ISshConnection which owns all resources.
/// Callers must dispose the connection when done. Authentication resources
/// (like loaded private keys) are tracked and disposed with the connection.
/// </para>
/// </remarks>
public sealed class SshConnectionService : ISshConnectionService
{
    // Buffer size for ShellStream - 4KB provides good balance between
    // memory usage and reducing syscall overhead for typical terminal I/O
    private const int DefaultBufferSize = 4096;

    // xterm-256color provides the best compatibility with modern TUI apps
    // (vim, htop, tmux) while supporting full 256-color palette
    private const string DefaultTerminalName = "xterm-256color";

    private const int MinPort = 1;
    private const int MaxPort = 65535;

    private readonly ILogger<SshConnectionService> _logger;
    private readonly ITerminalResizeService _resizeService;
    private readonly ISshAuthenticationFactory _authFactory;
    private readonly IProxyChainConnectionBuilder _proxyChainBuilder;
    private readonly IConnectionRetryPolicy _retryPolicy;

    public SshConnectionService(
        ISshAuthenticationFactory authFactory,
        ILogger<SshConnectionService>? logger = null,
        ITerminalResizeService? resizeService = null,
        IProxyChainConnectionBuilder? proxyChainBuilder = null,
        IConnectionRetryPolicy? retryPolicy = null)
    {
        _authFactory = authFactory ?? throw new ArgumentNullException(nameof(authFactory));
        _logger = logger ?? NullLogger<SshConnectionService>.Instance;
        _resizeService = resizeService ?? new TerminalResizeService();
        _proxyChainBuilder = proxyChainBuilder ?? new ProxyChainConnectionBuilder(authFactory, null);
        _retryPolicy = retryPolicy ?? new ConnectionRetryPolicy(null, ConnectionRetryOptions.NoRetry);
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
        ValidateConnectionInfo(connectionInfo);

        _logger.LogInformation("Connecting to {Host}:{Port} as {Username} using {AuthType}",
            connectionInfo.Hostname, connectionInfo.Port, connectionInfo.Username, connectionInfo.AuthType);

        var authResult = _authFactory.CreateAuthMethods(connectionInfo, kbInteractiveCallback);
        var connInfo = new ConnectionInfo(
            connectionInfo.Hostname,
            connectionInfo.Port,
            connectionInfo.Username,
            authResult.Methods)
        {
            Timeout = connectionInfo.Timeout
        };

        // Configure key exchange algorithms to support more servers
        AlgorithmConfigurator.ConfigureAlgorithms(connInfo, _logger);

        var client = new SshClient(connInfo);

        if (connectionInfo.KeepAliveInterval.HasValue &&
            connectionInfo.KeepAliveInterval.Value > TimeSpan.Zero)
        {
            client.KeepAliveInterval = connectionInfo.KeepAliveInterval.Value;
        }

        // SECURITY: Log host key verification warnings
        LogHostKeyVerificationWarnings(connectionInfo, hostKeyCallback);

        var hostKeyVerificationResult = true;
        Exception? hostKeyException = null;

        // Set up host key verification if callback provided
        // IMPORTANT: Host key verification is a critical security feature that prevents MITM attacks.
        // Without it, an attacker could intercept the connection and impersonate the server.
        if (hostKeyCallback != null)
        {
            client.HostKeyReceived += (sender, e) =>
            {
                try
                {
                    // Compute SHA256 fingerprint for display - this format matches OpenSSH's output
                    // (e.g., "SHA256:AAAA...") making it easy for users to verify against known keys
                    var fingerprint = ComputeFingerprint(e.HostKey);
                    _logger.LogDebug("Received host key: {Algorithm} {Fingerprint}", e.HostKeyName, fingerprint);

                    // THREADING NOTE: SSH.NET's HostKeyReceived fires synchronously on an internal
                    // background thread during the TCP handshake - NOT the UI thread. This means:
                    // 1. Blocking here with GetAwaiter().GetResult() is safe (no UI deadlock)
                    // 2. The callback can safely show UI dialogs via Dispatcher.Invoke
                    // 3. We cannot make this handler async because SSH.NET expects a sync result
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
                        _logger.LogWarning("Host key rejected by user for {Host}:{Port}",
                            connectionInfo.Hostname, connectionInfo.Port);
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
            // Connect on background thread with retry policy for transient failures
            await _retryPolicy.ExecuteAsync(
                async _ =>
                {
                    await Task.Run(() => client.Connect(), ct);
                },
                $"SSH:{connectionInfo.Hostname}:{connectionInfo.Port}",
                connectionInfo.RetryOptions,
                ct);

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

            _logger.LogInformation("SSH connection established to {Host}:{Port}",
                connectionInfo.Hostname, connectionInfo.Port);
        }
        catch (Exception ex)
        {
            LogConnectionFailure(connectionInfo, connInfo, ex);
            client.Dispose();
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

        // Apply environment variables after shell is ready (only for POSIX-compatible shells)
        ApplyEnvironmentVariables(shellStream, connectionInfo.EnvironmentVariables, connectionInfo.ShellType);

        var connection = new SshConnection(client, shellStream, _logger, _resizeService);

        // Transfer ownership of disposable resources to the connection
        foreach (var disposable in authResult.Disposables)
        {
            connection.TrackDisposable(disposable);
        }

        return connection;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// ProxyJump (also known as jump host or bastion host) connections work by creating a chain
    /// of SSH connections, where each hop tunnels through the previous one using port forwarding.
    /// </para>
    /// <para>
    /// For example, to reach Target through Bastion → Jump:
    /// <code>
    /// 1. Connect directly to Bastion (first hop)
    /// 2. Create local port forward: localhost:random → Jump:22
    /// 3. Connect to Jump via localhost:random (tunneled through Bastion)
    /// 4. Create local port forward: localhost:random2 → Target:22
    /// 5. Connect to Target via localhost:random2 (tunneled through Jump→Bastion)
    /// </code>
    /// </para>
    /// <para>
    /// The resulting ProxyChainSshConnection owns all intermediate connections and port forwards,
    /// ensuring proper cleanup when the connection is disposed.
    /// </para>
    /// </remarks>
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

        // Optimization: Single hop doesn't need the complexity of port forwarding chains.
        // Just use the simpler direct connection path.
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

        // Use the builder to establish all intermediate connections
        var buildResult = await _proxyChainBuilder.BuildChainAsync(
            connectionChain,
            hostKeyCallback,
            kbInteractiveCallback,
            ct);

        // Create shell stream on target
        var shellStream = buildResult.TargetClient.CreateShellStream(
            terminalName: DefaultTerminalName,
            columns: columns,
            rows: rows,
            width: 0,
            height: 0,
            bufferSize: DefaultBufferSize);

        // Apply environment variables from the target (last) connection info
        var targetConnectionInfo = connectionChain[^1];
        ApplyEnvironmentVariables(shellStream, targetConnectionInfo.EnvironmentVariables, targetConnectionInfo.ShellType);

        // Create the chained connection wrapper
        var connection = new ProxyChainSshConnection(
            buildResult.TargetClient,
            shellStream,
            buildResult.IntermediateClients,
            buildResult.ForwardedPorts,
            _logger,
            _resizeService);

        // Track auth resources for cleanup
        foreach (var disposable in buildResult.Disposables)
        {
            connection.TrackDisposable(disposable);
        }

        _logger.LogInformation("Proxy chain connection established: {Chain}",
            string.Join(" → ", connectionChain.Select(c => c.Hostname)));

        return connection;
    }

    /// <summary>
    /// Validates the connection information parameters.
    /// </summary>
    private void ValidateConnectionInfo(TerminalConnectionInfo connectionInfo)
    {
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
    }

    /// <summary>
    /// Logs security warnings about host key verification configuration.
    /// </summary>
    private void LogHostKeyVerificationWarnings(
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback)
    {
        if (hostKeyCallback == null && !connectionInfo.SkipHostKeyVerification)
        {
            _logger.LogWarning(
                "SECURITY WARNING: Connecting to {Host}:{Port} without host key verification. " +
                "This makes the connection vulnerable to man-in-the-middle attacks. " +
                "Provide a HostKeyVerificationCallback or set SkipHostKeyVerification=true to acknowledge this risk.",
                connectionInfo.Hostname, connectionInfo.Port);
        }
        else if (connectionInfo.SkipHostKeyVerification)
        {
            _logger.LogWarning(
                "Host key verification explicitly disabled for {Host}:{Port}. " +
                "Connection is vulnerable to MITM attacks.",
                connectionInfo.Hostname, connectionInfo.Port);
        }
    }

    /// <summary>
    /// Logs detailed information about a connection failure.
    /// </summary>
    private void LogConnectionFailure(
        TerminalConnectionInfo connectionInfo,
        ConnectionInfo connInfo,
        Exception ex)
    {
        _logger.LogError("Connection to {Host}:{Port} failed: {Message}",
            connectionInfo.Hostname, connectionInfo.Port, ex.Message);
        _logger.LogError("Offered KEX algorithms: {Algorithms}",
            string.Join(", ", connInfo.KeyExchangeAlgorithms.Keys));
        _logger.LogError("Offered host key algorithms: {Algorithms}",
            string.Join(", ", connInfo.HostKeyAlgorithms.Keys));
        _logger.LogError("Offered encryptions: {Algorithms}",
            string.Join(", ", connInfo.Encryptions.Keys));
        _logger.LogError(ex, "Full exception details");
    }

    /// <summary>
    /// Disposes authentication resources when connection fails.
    /// </summary>
    private void DisposeAuthResources(SshAuthenticationResult authResult)
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
    /// Applies environment variables to the shell stream by sending export commands.
    /// </summary>
    /// <param name="shellStream">The shell stream to write to.</param>
    /// <param name="environmentVariables">The environment variables to apply.</param>
    /// <param name="shellType">The shell type to determine if and how to apply variables.</param>
    /// <remarks>
    /// <para>
    /// Environment variables are applied by sending POSIX-compliant export commands
    /// to the shell. Values are properly escaped to prevent command injection.
    /// </para>
    /// <para>
    /// The export commands are sent immediately after the shell is created,
    /// before any user interaction. They will appear in the terminal output.
    /// </para>
    /// <para>
    /// For non-POSIX shells (PowerShell, CMD, network appliances), environment
    /// variables are skipped to avoid command errors or unexpected behavior.
    /// </para>
    /// </remarks>
    private void ApplyEnvironmentVariables(
        ShellStream shellStream,
        IReadOnlyList<EnvironmentVariableEntry> environmentVariables,
        ShellType shellType)
    {
        if (environmentVariables.Count == 0)
        {
            return;
        }

        // Check if environment variables should be applied for this shell type
        if (!ShouldApplyEnvironmentVariables(shellType))
        {
            _logger.LogDebug(
                "Skipping {Count} environment variable(s) - shell type {ShellType} does not support POSIX export syntax",
                environmentVariables.Count, shellType);
            return;
        }

        _logger.LogDebug("Applying {Count} environment variable(s) to {ShellType} shell",
            environmentVariables.Count, shellType);

        foreach (var envVar in environmentVariables)
        {
            // Validate name follows POSIX naming convention to prevent injection
            if (!IsValidPosixName(envVar.Name))
            {
                _logger.LogWarning(
                    "Skipping environment variable with invalid name: {Name}",
                    envVar.Name);
                continue;
            }

            // Escape the value to prevent command injection
            var escapedValue = EscapeShellValue(envVar.Value);

            // Send export command through the shell
            // Using double quotes allows variable expansion if needed
            var exportCommand = $"export {envVar.Name}=\"{escapedValue}\"";
            shellStream.WriteLine(exportCommand);

            _logger.LogDebug("Applied environment variable: {Name}", envVar.Name);
        }
    }

    /// <summary>
    /// Validates that a string is a valid POSIX environment variable name.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <returns>True if the name is valid, false otherwise.</returns>
    /// <remarks>
    /// POSIX environment variable names must:
    /// - Start with a letter (a-z, A-Z) or underscore (_)
    /// - Contain only letters, digits (0-9), or underscores
    /// - Not be empty
    /// </remarks>
    private static bool IsValidPosixName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // First character must be letter or underscore
        var first = name[0];
        if (!char.IsLetter(first) && first != '_')
        {
            return false;
        }

        // Remaining characters must be letters, digits, or underscores
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Escapes a value for safe inclusion in a double-quoted shell string.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value safe for use in double quotes.</returns>
    /// <remarks>
    /// <para>
    /// In double-quoted strings, the following characters have special meaning and must be escaped:
    /// - Backslash (\) - escape character
    /// - Double quote (") - string delimiter
    /// - Dollar sign ($) - variable/command expansion
    /// - Backtick (`) - command substitution
    /// - Exclamation mark (!) - history expansion (bash-specific, but safe to escape)
    /// </para>
    /// <para>
    /// Control characters (0x00-0x1F) are escaped or removed to prevent:
    /// - String truncation (NULL bytes)
    /// - Terminal escape sequence injection
    /// - Unexpected command execution via special control sequences
    /// </para>
    /// </remarks>
    private static string EscapeShellValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 16);

        foreach (var c in value)
        {
            switch (c)
            {
                // Characters that have special meaning in double-quoted strings
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '$':
                    sb.Append("\\$");
                    break;
                case '`':
                    sb.Append("\\`");
                    break;
                case '!':
                    // Bash history expansion - escape to be safe
                    sb.Append("\\!");
                    break;

                // Control characters - escape to prevent injection and for cleaner output
                case '\0':
                    // NULL byte - skip entirely as it truncates C strings
                    // This is a defense-in-depth measure; NULL should already be removed by EnvironmentVariableEntry.TryCreate
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\a': // Bell (0x07)
                    sb.Append("\\a");
                    break;
                case '\b': // Backspace (0x08)
                    sb.Append("\\b");
                    break;
                case '\f': // Form feed (0x0C)
                    sb.Append("\\f");
                    break;
                case '\v': // Vertical tab (0x0B)
                    sb.Append("\\v");
                    break;
                case '\x1B': // Escape (0x1B) - prevents ANSI escape sequence injection
                    sb.Append("\\e");
                    break;

                default:
                    // Handle remaining control characters (0x01-0x06, 0x0E-0x1A, 0x1C-0x1F)
                    if (c < 0x20)
                    {
                        // Escape as octal for maximum shell compatibility
                        sb.Append($"\\{Convert.ToString(c, 8).PadLeft(3, '0')}");
                    }
                    else
                    {
                        // All other characters are safe in double quotes
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Determines if environment variables should be applied for the given shell type.
    /// </summary>
    /// <param name="shellType">The shell type.</param>
    /// <returns>True if environment variables should be applied using export syntax.</returns>
    private static bool ShouldApplyEnvironmentVariables(ShellType shellType)
    {
        return shellType switch
        {
            ShellType.Auto => true,   // Assume POSIX-compliant shell
            ShellType.Posix => true,  // Explicitly POSIX
            ShellType.PowerShell => false, // Would need $env:VAR = "value" syntax
            ShellType.Cmd => false,        // Would need set VAR=value syntax
            ShellType.NetworkAppliance => false, // No env var support
            ShellType.Other => false,      // Unknown, skip to be safe
            _ => false
        };
    }
}
