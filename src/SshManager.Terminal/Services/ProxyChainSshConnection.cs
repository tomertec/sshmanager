using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SshManager.Terminal.Services;

/// <summary>
/// Wraps a proxy chain SSH connection that manages multiple chained clients and port forwards.
/// Disposing this connection cleans up all intermediate connections in reverse order.
/// </summary>
internal sealed class ProxyChainSshConnection : SshConnectionBase
{
    private readonly IReadOnlyList<SshClient> _intermediateClients;
    private readonly IReadOnlyList<ForwardedPortLocal> _forwardedPorts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyChainSshConnection"/> class.
    /// </summary>
    /// <param name="targetClient">The SSH client connected to the final target host.</param>
    /// <param name="shellStream">The shell stream for terminal I/O.</param>
    /// <param name="intermediateClients">The intermediate SSH clients in the proxy chain.</param>
    /// <param name="forwardedPorts">The local port forwards used for tunneling.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="resizeService">Service for terminal resize operations.</param>
    public ProxyChainSshConnection(
        SshClient targetClient,
        ShellStream shellStream,
        IReadOnlyList<SshClient> intermediateClients,
        IReadOnlyList<ForwardedPortLocal> forwardedPorts,
        ILogger logger,
        ITerminalResizeService resizeService)
        : base(targetClient, shellStream, logger, resizeService)
    {
        _intermediateClients = intermediateClients ?? throw new ArgumentNullException(nameof(intermediateClients));
        _forwardedPorts = forwardedPorts ?? throw new ArgumentNullException(nameof(forwardedPorts));

        // Monitor intermediate connections for failures
        foreach (var client in _intermediateClients)
        {
            client.ErrorOccurred += OnIntermediateError;
        }
    }

    /// <summary>
    /// Handles errors from intermediate SSH clients in the proxy chain.
    /// </summary>
    private void OnIntermediateError(object? sender, ExceptionEventArgs e)
    {
        Logger.LogWarning(e.Exception, "Proxy chain intermediate connection error occurred");
        RaiseDisconnected();
    }

    /// <inheritdoc />
    protected override void OnClientError(object? sender, ExceptionEventArgs e)
    {
        Logger.LogWarning(e.Exception, "Proxy chain target connection error occurred");
        RaiseDisconnected();
    }

    /// <inheritdoc />
    protected override void OnStreamClosed(object? sender, EventArgs e)
    {
        Logger.LogInformation("Proxy chain shell stream closed");
        RaiseDisconnected();
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        Logger.LogDebug("Disposing proxy chain SSH connection ({HopCount} intermediate hops)",
            _intermediateClients.Count);

        // Unsubscribe from intermediate client events
        foreach (var client in _intermediateClients)
        {
            client.ErrorOccurred -= OnIntermediateError;
        }

        // Unsubscribe and dispose the primary (target) client via base class
        // Note: Base class handles Client event unsubscription, stream, and client disposal
        Client.ErrorOccurred -= OnClientError;
        ShellStream.Closed -= OnStreamClosed;

        // Dispose shell stream first
        try
        {
            ShellStream.Dispose();
            Logger.LogDebug("Proxy chain shell stream disposed");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error disposing proxy chain shell stream");
        }

        // Dispose target client
        try
        {
            if (Client.IsConnected)
            {
                Client.Disconnect();
            }
            Client.Dispose();
            Logger.LogDebug("Proxy chain target client disposed");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error disposing proxy chain target client");
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
                Logger.LogDebug(ex, "Error disposing forwarded port {Index}", i);
            }
        }
        Logger.LogDebug("Proxy chain forwarded ports disposed");

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
                Logger.LogDebug(ex, "Error disposing intermediate client {Index}", i);
            }
        }
        Logger.LogDebug("Proxy chain intermediate clients disposed");

        // Dispose tracked resources (PrivateKeyFile instances)
        DisposeTrackedResources();
    }
}
