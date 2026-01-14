using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Builder for establishing proxy chain SSH connections through multiple hops.
/// Each hop establishes an SSH connection through the previous hop's forwarded port.
/// </summary>
public class ProxyChainConnectionBuilder : IProxyChainConnectionBuilder
{
    private readonly ISshAuthenticationFactory _authFactory;
    private readonly ILogger<ProxyChainConnectionBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyChainConnectionBuilder"/> class.
    /// </summary>
    /// <param name="authFactory">Factory for creating SSH authentication methods.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public ProxyChainConnectionBuilder(
        ISshAuthenticationFactory authFactory,
        ILogger<ProxyChainConnectionBuilder>? logger = null)
    {
        _authFactory = authFactory ?? throw new ArgumentNullException(nameof(authFactory));
        _logger = logger ?? NullLogger<ProxyChainConnectionBuilder>.Instance;
    }

    /// <inheritdoc />
    public async Task<ProxyChainBuildResult> BuildChainAsync(
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken ct)
    {
        if (connectionChain.Count < 2)
        {
            throw new ArgumentException(
                "Connection chain must have at least 2 entries for proxy chain.",
                nameof(connectionChain));
        }

        _logger.LogInformation("Building proxy chain with {HopCount} hops", connectionChain.Count);

        // Track all intermediate connections and resources for cleanup
        var intermediateClients = new List<SshClient>();
        var forwardedPorts = new List<ForwardedPortLocal>();
        var disposables = new List<IDisposable>();

        try
        {
            int currentLocalPort = 0;

            // Connect through each hop except the last (which is the target)
            for (int i = 0; i < connectionChain.Count - 1; i++)
            {
                var hopInfo = connectionChain[i];
                var nextHopInfo = connectionChain[i + 1];

                _logger.LogDebug("Connecting to hop {Index}: {Host}:{Port}",
                    i + 1, hopInfo.Hostname, hopInfo.Port);

                // Create connection info and auth methods
                var authResult = _authFactory.CreateAuthMethods(hopInfo, kbInteractiveCallback);
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

                AlgorithmConfigurator.ConfigureAlgorithms(connInfo, _logger);

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

            var targetAuthResult = _authFactory.CreateAuthMethods(targetInfo, kbInteractiveCallback);
            disposables.AddRange(targetAuthResult.Disposables);

            var targetConnInfo = new ConnectionInfo(
                "127.0.0.1",
                currentLocalPort,
                targetInfo.Username,
                targetAuthResult.Methods)
            {
                Timeout = targetInfo.Timeout
            };

            AlgorithmConfigurator.ConfigureAlgorithms(targetConnInfo, _logger);

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

            _logger.LogInformation("Proxy chain built successfully: {Chain}",
                string.Join(" → ", connectionChain.Select(c => c.Hostname)));

            return new ProxyChainBuildResult(
                targetClient,
                currentLocalPort,
                intermediateClients.AsReadOnly(),
                forwardedPorts.AsReadOnly(),
                disposables.AsReadOnly());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build proxy chain connection");

            // Clean up on failure
            CleanupOnFailure(forwardedPorts, intermediateClients, disposables);

            throw;
        }
    }

    /// <summary>
    /// Sets up host key verification for a client.
    /// Logs security warnings if verification is not configured properly.
    /// </summary>
    private void SetupHostKeyVerification(
        SshClient client,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback)
    {
        // SECURITY: Check host key verification configuration for this hop
        if (hostKeyCallback == null && !connectionInfo.SkipHostKeyVerification)
        {
            _logger.LogWarning(
                "SECURITY WARNING: Connecting to {Host}:{Port} without host key verification. " +
                "This hop is vulnerable to man-in-the-middle attacks.",
                connectionInfo.Hostname, connectionInfo.Port);
        }
        else if (connectionInfo.SkipHostKeyVerification)
        {
            _logger.LogWarning(
                "Host key verification explicitly disabled for hop {Host}:{Port}.",
                connectionInfo.Hostname, connectionInfo.Port);
        }

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
    /// Cleans up resources on failure.
    /// </summary>
    private void CleanupOnFailure(
        List<ForwardedPortLocal> forwardedPorts,
        List<SshClient> intermediateClients,
        List<IDisposable> disposables)
    {
        foreach (var port in forwardedPorts)
        {
            try
            {
                port.Stop();
                port.Dispose();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "Error disposing forwarded port during cleanup");
            }
        }

        foreach (var client in intermediateClients)
        {
            try
            {
                client.Disconnect();
                client.Dispose();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "Error disposing intermediate client during cleanup");
            }
        }

        foreach (var d in disposables)
        {
            try
            {
                d.Dispose();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "Error disposing auth resource during cleanup");
            }
        }
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
    /// Computes the SHA256 fingerprint of a host key in base64 format.
    /// </summary>
    private static string ComputeFingerprint(byte[] hostKey)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(hostKey);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }
}
