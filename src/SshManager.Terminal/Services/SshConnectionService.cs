using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
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
    private readonly ISshAuthenticationFactory _authFactory;
    private readonly IProxyChainConnectionBuilder _proxyChainBuilder;

    public SshConnectionService(
        ISshAuthenticationFactory authFactory,
        ILogger<SshConnectionService>? logger = null,
        ITerminalResizeService? resizeService = null,
        IProxyChainConnectionBuilder? proxyChainBuilder = null)
    {
        _authFactory = authFactory ?? throw new ArgumentNullException(nameof(authFactory));
        _logger = logger ?? NullLogger<SshConnectionService>.Instance;
        _resizeService = resizeService ?? new TerminalResizeService();
        _proxyChainBuilder = proxyChainBuilder ?? new ProxyChainConnectionBuilder(authFactory, null);
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
}
