using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handler for terminal connection operations.
/// Manages SSH connection establishment and bridge creation.
/// </summary>
public sealed class TerminalConnectionHandler : ITerminalConnectionHandler
{
    private readonly ILogger<TerminalConnectionHandler> _logger;

    public TerminalConnectionHandler(ILogger<TerminalConnectionHandler>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalConnectionHandler>.Instance;
    }

    /// <inheritdoc />
    public async Task<TerminalConnectionResult> ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sshService);
        ArgumentNullException.ThrowIfNull(connectionInfo);

        _logger.LogInformation("Connecting to {Host}:{Port}", connectionInfo.Hostname, connectionInfo.Port);

        // Establish SSH connection
        var connection = await sshService.ConnectAsync(
            connectionInfo,
            hostKeyCallback,
            kbInteractiveCallback,
            columns,
            rows,
            ct);

        // Create SSH bridge for data flow with connection health monitoring
        // The health check uses TrySendKeepAlive() which sends an actual packet to detect stale connections
        // (e.g., when remote host reboots during an idle session)
        var bridge = new SshTerminalBridge(
            connection.ShellStream,
            logger: null,
            connectionHealthCheck: () => connection.TrySendKeepAlive());

        _logger.LogInformation("Connected to {Host}", connectionInfo.Hostname);

        return new TerminalConnectionResult(connection, bridge);
    }

    /// <inheritdoc />
    public async Task<TerminalConnectionResult> ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sshService);
        if (connectionChain == null || connectionChain.Count == 0)
        {
            throw new ArgumentException("Connection chain cannot be empty", nameof(connectionChain));
        }

        var targetHost = connectionChain[^1];
        _logger.LogInformation("Connecting to {Host} through proxy chain with {HopCount} hops",
            targetHost.Hostname, connectionChain.Count);

        // Establish SSH connection through proxy chain
        var connection = await sshService.ConnectWithProxyChainAsync(
            connectionChain,
            hostKeyCallback,
            kbInteractiveCallback,
            columns,
            rows,
            ct);

        // Create SSH bridge for data flow with connection health monitoring
        // The health check uses TrySendKeepAlive() which sends an actual packet to detect stale connections
        var bridge = new SshTerminalBridge(
            connection.ShellStream,
            logger: null,
            connectionHealthCheck: () => connection.TrySendKeepAlive());

        var hosts = string.Join(" → ", connectionChain.Select(c => c.Hostname));
        _logger.LogInformation("Connected through proxy chain: {Chain}", hosts);

        return new TerminalConnectionResult(connection, bridge);
    }

    /// <inheritdoc />
    public TerminalAttachResult AttachToSession(TerminalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.IsConnected)
        {
            _logger.LogDebug("Session {Title} is not connected", session.Title);
            return new TerminalAttachResult(null, false, false);
        }

        // Reuse existing bridge from session if available (for mirroring)
        // This prevents multiple readers from competing on the same ShellStream
        if (session.Bridge != null)
        {
            _logger.LogDebug("Attaching to existing bridge for mirrored session: {Title}", session.Title);
            return new TerminalAttachResult(session.Bridge, OwnsBridge: false, NeedsStartReading: false);
        }

        // Fallback: create new bridge if session doesn't have one
        if (session.Connection?.ShellStream != null)
        {
            var connection = session.Connection;
            var bridge = new SshTerminalBridge(
                connection.ShellStream,
                logger: null,
                connectionHealthCheck: () => connection.TrySendKeepAlive());
            session.Bridge = bridge;
            _logger.LogDebug("Created new bridge for session: {Title}", session.Title);
            return new TerminalAttachResult(bridge, OwnsBridge: true, NeedsStartReading: true);
        }

        _logger.LogWarning("Session {Title} has no valid connection", session.Title);
        return new TerminalAttachResult(null, false, false);
    }

    /// <inheritdoc />
    public void Disconnect(SshTerminalBridge? bridge, bool ownsBridge)
    {
        if (bridge == null) return;

        // Only dispose the bridge if this caller owns it
        // Shared bridges are disposed when the session closes
        if (ownsBridge)
        {
            bridge.Dispose();
            _logger.LogDebug("Bridge disposed");
        }
        else
        {
            _logger.LogDebug("Bridge not disposed (shared)");
        }
    }
}
