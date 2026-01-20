using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for building and executing SSH tunnel chains from visual profiles.
/// </summary>
/// <remarks>
/// This service validates tunnel graph configurations, generates SSH command equivalents,
/// and executes complex tunnel chains using SSH.NET port forwarding capabilities.
/// Thread-safe for concurrent tunnel operations.
/// </remarks>
public sealed class TunnelBuilderService : ITunnelBuilderService
{
    private readonly ISshConnectionService _connectionService;
    private readonly IHostRepository _hostRepository;
    private readonly ILogger<TunnelBuilderService> _logger;
    private readonly ConcurrentDictionary<Guid, ActiveTunnel> _activeTunnels = new();

    public TunnelBuilderService(
        ISshConnectionService connectionService,
        IHostRepository hostRepository,
        ILogger<TunnelBuilderService>? logger = null)
    {
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _hostRepository = hostRepository ?? throw new ArgumentNullException(nameof(hostRepository));
        _logger = logger ?? NullLogger<TunnelBuilderService>.Instance;
    }

    /// <inheritdoc />
    public TunnelValidationResult Validate(TunnelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<string>();
        var warnings = new List<string>();

        // Check minimum node count
        if (profile.Nodes.Count < 2)
        {
            errors.Add("Tunnel profile must have at least 2 nodes (source and target).");
        }

        // Check for LocalMachine node
        var localMachineNodes = profile.Nodes.Where(n => n.NodeType == TunnelNodeType.LocalMachine).ToList();
        if (localMachineNodes.Count == 0)
        {
            errors.Add("Tunnel profile must have a LocalMachine node as the starting point.");
        }
        else if (localMachineNodes.Count > 1)
        {
            errors.Add("Tunnel profile can only have one LocalMachine node.");
        }

        // Validate node configurations
        foreach (var node in profile.Nodes)
        {
            ValidateNode(node, errors, warnings);
        }

        // Validate edges
        foreach (var edge in profile.Edges)
        {
            ValidateEdge(edge, profile.Nodes, errors, warnings);
        }

        // Check for circular dependencies
        if (profile.Edges.Any())
        {
            if (HasCircularDependencies(profile))
            {
                errors.Add("Tunnel graph contains circular dependencies. Ensure connections flow in one direction.");
            }
        }

        // Check graph connectivity (all nodes should be reachable from LocalMachine)
        if (localMachineNodes.Count == 1)
        {
            var reachableNodes = GetReachableNodes(localMachineNodes[0].Id, profile);
            var unreachableNodes = profile.Nodes
                .Where(n => !reachableNodes.Contains(n.Id) && n.Id != localMachineNodes[0].Id)
                .ToList();

            foreach (var node in unreachableNodes)
            {
                warnings.Add($"Node '{node.Label}' is not reachable from LocalMachine.");
            }
        }

        return new TunnelValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors.AsReadOnly(),
            Warnings: warnings.AsReadOnly());
    }

    /// <inheritdoc />
    public async Task<string> GenerateSshCommandAsync(TunnelProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var validationResult = Validate(profile);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(
                $"Cannot generate SSH command for invalid profile. Errors: {string.Join(", ", validationResult.Errors)}");
        }

        var sb = new StringBuilder();
        var localMachineNode = profile.Nodes.First(n => n.NodeType == TunnelNodeType.LocalMachine);

        // Build tunnel chain by traversing the graph
        var tunnelChain = BuildTunnelChain(profile, localMachineNode);

        // Generate command for each hop
        var sshHosts = tunnelChain.Where(n => n.NodeType == TunnelNodeType.SshHost).ToList();
        if (sshHosts.Count == 0)
        {
            return "# No SSH hosts in tunnel chain";
        }

        // Load host entries to get actual connection information
        var hostEntries = new Dictionary<Guid, HostEntry>();
        foreach (var hostNode in sshHosts)
        {
            if (hostNode.HostId.HasValue)
            {
                var hostEntry = await _hostRepository.GetByIdAsync(hostNode.HostId.Value, ct);
                if (hostEntry is not null)
                {
                    hostEntries[hostNode.HostId.Value] = hostEntry;
                }
            }
        }

        // Build the SSH command: ssh -J proxy1,proxy2,... target
        // Last host in chain is target, all previous are proxies
        if (sshHosts.Count == 1)
        {
            // Single host - direct connection
            var host = sshHosts[0];
            if (host.HostId.HasValue && hostEntries.TryGetValue(host.HostId.Value, out var hostEntry))
            {
                sb.Append($"ssh {FormatSshHost(hostEntry)}");
            }
            else
            {
                sb.Append($"ssh {host.Label}");
            }
        }
        else
        {
            // Multi-hop: last host is target, rest are proxies
            var targetHost = sshHosts[^1]; // Last element
            var proxyHosts = sshHosts.Take(sshHosts.Count - 1).ToList();

            // Add ProxyJump option with all intermediate hosts
            sb.Append("ssh -J ");
            var proxyParts = new List<string>();
            foreach (var proxy in proxyHosts)
            {
                if (proxy.HostId.HasValue && hostEntries.TryGetValue(proxy.HostId.Value, out var proxyEntry))
                {
                    proxyParts.Add(FormatSshHost(proxyEntry));
                }
                else
                {
                    proxyParts.Add(proxy.Label);
                }
            }
            sb.Append(string.Join(",", proxyParts));
            sb.Append(' ');

            // Add target host
            if (targetHost.HostId.HasValue && hostEntries.TryGetValue(targetHost.HostId.Value, out var targetEntry))
            {
                sb.Append(FormatSshHost(targetEntry));
            }
            else
            {
                sb.Append(targetHost.Label);
            }
        }

        // Add port forwarding options
        var forwardingNodes = tunnelChain.Where(n =>
            n.NodeType == TunnelNodeType.LocalPort ||
            n.NodeType == TunnelNodeType.RemotePort ||
            n.NodeType == TunnelNodeType.DynamicProxy).ToList();

        foreach (var node in forwardingNodes)
        {
            if (node.NodeType == TunnelNodeType.LocalPort)
            {
                // Local forward: -L local_port:remote_host:remote_port
                if (node.LocalPort.HasValue && node.RemotePort.HasValue)
                {
                    var remoteHost = node.RemoteHost ?? "localhost";
                    sb.Append($" -L {node.LocalPort}:{remoteHost}:{node.RemotePort}");
                }
            }
            else if (node.NodeType == TunnelNodeType.RemotePort)
            {
                // Remote forward: -R [bind_address:]remote_port:target_host:target_port
                // bind_address: interface on remote server to bind to (optional)
                // remote_port: port on remote server to listen on
                // target_host: where to forward connections to (from local machine's perspective)
                // target_port: port on the target host
                if (node.RemotePort.HasValue && node.LocalPort.HasValue)
                {
                    var targetHost = GetTargetHostForRemoteForward(node, profile) ?? node.RemoteHost ?? "localhost";

                    if (!string.IsNullOrWhiteSpace(node.BindAddress))
                    {
                        sb.Append($" -R {node.BindAddress}:{node.RemotePort}:{targetHost}:{node.LocalPort}");
                    }
                    else
                    {
                        sb.Append($" -R {node.RemotePort}:{targetHost}:{node.LocalPort}");
                    }
                }
            }
            else if (node.NodeType == TunnelNodeType.DynamicProxy)
            {
                // Dynamic forward: -D local_port
                if (node.LocalPort.HasValue)
                {
                    sb.Append($" -D {node.LocalPort}");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the target host for remote port forwarding by checking for connected TargetHost nodes.
    /// </summary>
    /// <param name="remotePortNode">The RemotePort node to find targets for.</param>
    /// <param name="profile">The tunnel profile containing the graph.</param>
    /// <returns>The target hostname from a connected TargetHost node, or null if none found.</returns>
    private static string? GetTargetHostForRemoteForward(TunnelNode remotePortNode, TunnelProfile profile)
    {
        // Find edges where this RemotePort node is the source
        var outgoingEdges = profile.Edges.Where(e => e.SourceNodeId == remotePortNode.Id);

        // Look for a connected TargetHost node
        foreach (var edge in outgoingEdges)
        {
            var targetNode = profile.Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);
            if (targetNode?.NodeType == TunnelNodeType.TargetHost && !string.IsNullOrWhiteSpace(targetNode.RemoteHost))
            {
                return targetNode.RemoteHost;
            }
        }

        // Also check incoming edges (in case TargetHost is connected TO the RemotePort node)
        var incomingEdges = profile.Edges.Where(e => e.TargetNodeId == remotePortNode.Id);
        foreach (var edge in incomingEdges)
        {
            var sourceNode = profile.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
            if (sourceNode?.NodeType == TunnelNodeType.TargetHost && !string.IsNullOrWhiteSpace(sourceNode.RemoteHost))
            {
                return sourceNode.RemoteHost;
            }
        }

        return null;
    }

    /// <summary>
    /// Formats a host entry as user@hostname:port for SSH command.
    /// </summary>
    private static string FormatSshHost(HostEntry host)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(host.Username))
        {
            // Security: Sanitize username to prevent command injection
            sb.Append(SanitizeSshIdentifier(host.Username));
            sb.Append('@');
        }

        // Security: Sanitize hostname to prevent command injection
        sb.Append(SanitizeSshIdentifier(host.Hostname));

        if (host.Port != 22)
        {
            sb.Append(':');
            sb.Append(host.Port);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes SSH identifiers (usernames and hostnames) to prevent command injection.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <returns>A sanitized version of the input that is safe for use in SSH commands.</returns>
    /// <remarks>
    /// Security rationale: SSH command strings can be vulnerable to injection attacks if usernames
    /// or hostnames contain shell-dangerous characters like semicolons, backticks, dollar signs, etc.
    /// This method validates that identifiers only contain safe characters (alphanumeric, dots,
    /// hyphens, underscores, and @ symbol). If dangerous characters are detected, the value is
    /// rejected to prevent command injection vulnerabilities.
    /// </remarks>
    private static string SanitizeSshIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // Allow only: alphanumeric, dot, hyphen, underscore, @ symbol, and IPv6 brackets
        // This prevents command injection via shell metacharacters
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) &&
                ch != '.' &&
                ch != '-' &&
                ch != '_' &&
                ch != '@' &&
                ch != '[' &&  // IPv6 support
                ch != ']' &&  // IPv6 support
                ch != ':')    // IPv6 support
            {
                throw new ArgumentException(
                    $"Invalid character '{ch}' detected in SSH identifier '{value}'. " +
                    "Only alphanumeric characters, dots, hyphens, underscores, @ symbols, and IPv6 brackets are allowed.",
                    nameof(value));
            }
        }

        return value;
    }

    /// <summary>
    /// Validates whether a string is a valid hostname or IP address.
    /// </summary>
    /// <param name="hostname">The hostname or IP address to validate.</param>
    /// <returns>True if valid hostname or IP address, false otherwise.</returns>
    /// <remarks>
    /// Hostname validation follows RFC 1123:
    /// - Maximum length: 253 characters
    /// - Each label (between dots) max 63 characters
    /// - Labels must start with alphanumeric
    /// - Labels can contain alphanumeric, hyphens
    /// - Labels cannot start or end with hyphen
    /// Also accepts IPv4 and IPv6 addresses.
    /// </remarks>
    private static bool IsValidHostnameOrIpAddress(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return false;
        }

        // Check if it's an IP address first
        if (IPAddress.TryParse(hostname, out _))
        {
            return true;
        }

        // Validate as hostname (RFC 1123)
        if (hostname.Length > 253)
        {
            return false;
        }

        // Split into labels (parts between dots)
        var labels = hostname.Split('.');

        // RFC 1123 compliant label pattern:
        // - Must start with alphanumeric
        // - Can contain alphanumeric and hyphens
        // - Must end with alphanumeric (if length > 1)
        // - Max 63 characters
        var validLabelPattern = new Regex(@"^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", RegexOptions.Compiled);

        return labels.All(label =>
            !string.IsNullOrEmpty(label) &&
            label.Length <= 63 &&
            validLabelPattern.IsMatch(label));
    }

    /// <inheritdoc />
    public async Task<TunnelExecutionResult> ExecuteAsync(
        TunnelProfile profile,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Validate first
        var validationResult = Validate(profile);
        if (!validationResult.IsValid)
        {
            var errorMessage = $"Tunnel validation failed: {string.Join(", ", validationResult.Errors)}";
            _logger.LogError(errorMessage);
            return new TunnelExecutionResult(false, errorMessage, null);
        }

        var sessionId = Guid.NewGuid();

        try
        {
            _logger.LogInformation("Executing tunnel profile: {ProfileName} (Session: {SessionId})",
                profile.DisplayName, sessionId);

            var localMachineNode = profile.Nodes.First(n => n.NodeType == TunnelNodeType.LocalMachine);
            var tunnelChain = BuildTunnelChain(profile, localMachineNode);

            // Extract SSH hosts for connection chain
            var sshHostNodes = tunnelChain.Where(n => n.NodeType == TunnelNodeType.SshHost).ToList();
            if (sshHostNodes.Count == 0)
            {
                return new TunnelExecutionResult(false, "No SSH hosts found in tunnel chain", null);
            }

            // Build connection chain
            var connectionChain = new List<TerminalConnectionInfo>();
            foreach (var hostNode in sshHostNodes)
            {
                if (!hostNode.HostId.HasValue)
                {
                    return new TunnelExecutionResult(false, $"SSH host node '{hostNode.Label}' has no HostId", null);
                }

                var hostEntry = await _hostRepository.GetByIdAsync(hostNode.HostId.Value, ct);
                if (hostEntry is null)
                {
                    return new TunnelExecutionResult(false,
                        $"Host not found: {hostNode.HostId.Value}", null);
                }

                var connInfo = TerminalConnectionInfo.FromHostEntry(hostEntry);
                connectionChain.Add(connInfo);
            }

            // Establish connection (single hop or multi-hop)
            ISshConnection connection;
            if (connectionChain.Count == 1)
            {
                connection = await _connectionService.ConnectAsync(
                    connectionChain[0],
                    hostKeyCallback,
                    kbInteractiveCallback,
                    columns: 80,
                    rows: 24,
                    ct);
            }
            else
            {
                connection = await _connectionService.ConnectWithProxyChainAsync(
                    connectionChain,
                    hostKeyCallback,
                    kbInteractiveCallback,
                    columns: 80,
                    rows: 24,
                    ct);
            }

            // Set up port forwarding
            var forwardedPorts = new List<ForwardedPort>();
            var sshClient = GetSshClientFromConnection(connection);

            if (sshClient is not null)
            {
                var forwardingNodes = tunnelChain.Where(n =>
                    n.NodeType == TunnelNodeType.LocalPort ||
                    n.NodeType == TunnelNodeType.RemotePort ||
                    n.NodeType == TunnelNodeType.DynamicProxy).ToList();

                foreach (var node in forwardingNodes)
                {
                    ForwardedPort? forwardedPort = null;

                    if (node.NodeType == TunnelNodeType.LocalPort)
                    {
                        if (node.LocalPort.HasValue && node.RemotePort.HasValue)
                        {
                            // Security: Validate port numbers before casting to uint (prevents overflow)
                            ValidatePortNumber(node.LocalPort.Value, "LocalPort");
                            ValidatePortNumber(node.RemotePort.Value, "RemotePort");

                            var remoteHost = node.RemoteHost ?? "localhost";
                            var bindAddr = node.BindAddress ?? "127.0.0.1";
                            forwardedPort = new ForwardedPortLocal(
                                bindAddr,
                                (uint)node.LocalPort.Value,
                                remoteHost,
                                (uint)node.RemotePort.Value);
                        }
                    }
                    else if (node.NodeType == TunnelNodeType.RemotePort)
                    {
                        if (node.RemotePort.HasValue && node.LocalPort.HasValue)
                        {
                            // Security: Validate port numbers before casting to uint (prevents overflow)
                            ValidatePortNumber(node.RemotePort.Value, "RemotePort");
                            ValidatePortNumber(node.LocalPort.Value, "LocalPort");

                            // ForwardedPortRemote constructor: (boundHost, boundPort, host, port)
                            // boundHost: interface on remote server to bind to
                            // boundPort: port on remote server to listen on
                            // host: target host to forward connections to (from local machine's perspective)
                            // port: target port on the host
                            var targetHost = GetTargetHostForRemoteForward(node, profile) ?? node.RemoteHost ?? "127.0.0.1";

                            // Security: Default to localhost (127.0.0.1) binding to prevent unintended network exposure.
                            // Empty string would bind to all interfaces (0.0.0.0), which is a security risk.
                            // Users must explicitly set BindAddress to "0.0.0.0" or a specific interface to bind externally.
                            var boundHost = node.BindAddress ?? "127.0.0.1";

                            forwardedPort = new ForwardedPortRemote(
                                boundHost,
                                (uint)node.RemotePort.Value,
                                targetHost,
                                (uint)node.LocalPort.Value);
                        }
                    }
                    else if (node.NodeType == TunnelNodeType.DynamicProxy)
                    {
                        if (node.LocalPort.HasValue)
                        {
                            // Security: Validate port number before casting to uint (prevents overflow)
                            ValidatePortNumber(node.LocalPort.Value, "LocalPort");

                            var bindAddr = node.BindAddress ?? "127.0.0.1";
                            forwardedPort = new ForwardedPortDynamic(
                                bindAddr,
                                (uint)node.LocalPort.Value);
                        }
                    }

                    if (forwardedPort is not null)
                    {
                        sshClient.AddForwardedPort(forwardedPort);
                        forwardedPort.Start();
                        forwardedPorts.Add(forwardedPort);
                        _logger.LogDebug("Started port forwarding for node: {NodeLabel}", node.Label);
                    }
                }
            }

            // Track active tunnel
            var activeTunnel = new ActiveTunnel
            {
                ProfileId = profile.Id,
                SessionId = sessionId,
                DisplayName = profile.DisplayName,
                StartedAt = DateTimeOffset.UtcNow,
                Connection = connection,
                ForwardedPorts = forwardedPorts
            };

            _activeTunnels[profile.Id] = activeTunnel;

            _logger.LogInformation("Tunnel established successfully: {ProfileName} (Session: {SessionId})",
                profile.DisplayName, sessionId);

            return new TunnelExecutionResult(true, null, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tunnel profile: {ProfileName}", profile.DisplayName);
            return new TunnelExecutionResult(false, ex.Message, null);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(Guid profileId, CancellationToken ct = default)
    {
        if (!_activeTunnels.TryRemove(profileId, out var tunnel))
        {
            _logger.LogWarning("Tunnel not found or already stopped: {ProfileId}", profileId);
            return;
        }

        try
        {
            _logger.LogInformation("Stopping tunnel: {DisplayName} (Session: {SessionId})",
                tunnel.DisplayName, tunnel.SessionId);

            // Check for cancellation before proceeding
            ct.ThrowIfCancellationRequested();

            // Stop all forwarded ports
            foreach (var port in tunnel.ForwardedPorts)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    port.Stop();
                    port.Dispose();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Tunnel stop operation cancelled for: {DisplayName}", tunnel.DisplayName);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error stopping forwarded port");
                }
            }

            // Close connection with timeout handling
            if (tunnel.Connection is not null)
            {
                // Create a timeout token that combines with the cancellation token
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

                try
                {
                    await tunnel.Connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Connection disposal timed out after 30 seconds for tunnel: {DisplayName}", tunnel.DisplayName);
                }
            }

            _logger.LogInformation("Tunnel stopped: {DisplayName}", tunnel.DisplayName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Tunnel stop operation was cancelled for: {DisplayName}", tunnel.DisplayName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping tunnel: {DisplayName}", tunnel.DisplayName);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Guid, TunnelStatus> GetActiveTunnels()
    {
        // Presence in the dictionary indicates an active tunnel
        // Tunnels are removed from the dictionary when stopped via StopAsync
        return _activeTunnels.ToDictionary(
            kvp => kvp.Key,
            kvp => new TunnelStatus(
                kvp.Value.ProfileId,
                kvp.Value.DisplayName,
                kvp.Value.StartedAt,
                IsActive: true));
    }

    /// <summary>
    /// Validates an individual node configuration.
    /// </summary>
    private void ValidateNode(TunnelNode node, List<string> errors, List<string> warnings)
    {
        // Validate based on node type
        switch (node.NodeType)
        {
            case TunnelNodeType.LocalMachine:
                // No specific validation needed
                break;

            case TunnelNodeType.SshHost:
                if (!node.HostId.HasValue)
                {
                    errors.Add($"SSH host node '{node.Label}' must have a HostId.");
                }
                break;

            case TunnelNodeType.LocalPort:
                if (!node.LocalPort.HasValue)
                {
                    errors.Add($"Local port node '{node.Label}' must have a LocalPort.");
                }
                else if (node.LocalPort.Value < 1 || node.LocalPort.Value > 65535)
                {
                    errors.Add($"Local port node '{node.Label}' has invalid LocalPort: {node.LocalPort.Value}");
                }
                break;

            case TunnelNodeType.RemotePort:
                if (!node.RemotePort.HasValue)
                {
                    errors.Add($"Remote port node '{node.Label}' must have a RemotePort.");
                }
                else if (node.RemotePort.Value < 1 || node.RemotePort.Value > 65535)
                {
                    errors.Add($"Remote port node '{node.Label}' has invalid RemotePort: {node.RemotePort.Value}");
                }

                if (!node.LocalPort.HasValue)
                {
                    errors.Add($"Remote port node '{node.Label}' must have a LocalPort (target port).");
                }
                else if (node.LocalPort.Value < 1 || node.LocalPort.Value > 65535)
                {
                    errors.Add($"Remote port node '{node.Label}' has invalid LocalPort (target port): {node.LocalPort.Value}");
                }

                // Note: RemoteHost can be empty if a TargetHost node is connected instead
                if (string.IsNullOrWhiteSpace(node.RemoteHost))
                {
                    warnings.Add($"Remote port node '{node.Label}' should specify a RemoteHost or connect to a TargetHost node (defaults to localhost).");
                }
                break;

            case TunnelNodeType.TargetHost:
                if (string.IsNullOrWhiteSpace(node.RemoteHost))
                {
                    errors.Add($"Target host node '{node.Label}' must have a RemoteHost.");
                }
                else if (!IsValidHostnameOrIpAddress(node.RemoteHost))
                {
                    errors.Add($"Target host node '{node.Label}' has invalid RemoteHost format: '{node.RemoteHost}'. Must be a valid hostname or IP address.");
                }
                break;

            case TunnelNodeType.DynamicProxy:
                if (!node.LocalPort.HasValue)
                {
                    errors.Add($"SOCKS proxy node '{node.Label}' must have a LocalPort.");
                }
                else if (node.LocalPort.Value < 1 || node.LocalPort.Value > 65535)
                {
                    errors.Add($"SOCKS proxy node '{node.Label}' has invalid LocalPort: {node.LocalPort.Value}");
                }
                break;
        }
    }

    /// <summary>
    /// Validates an edge connection between nodes.
    /// </summary>
    private void ValidateEdge(TunnelEdge edge, ICollection<TunnelNode> nodes, List<string> errors, List<string> warnings)
    {
        var sourceNode = nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
        var targetNode = nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);

        if (sourceNode is null)
        {
            errors.Add($"Edge references non-existent source node: {edge.SourceNodeId}");
        }

        if (targetNode is null)
        {
            errors.Add($"Edge references non-existent target node: {edge.TargetNodeId}");
        }

        // Check for self-loops
        if (edge.SourceNodeId == edge.TargetNodeId)
        {
            errors.Add($"Edge cannot connect a node to itself: {edge.SourceNodeId}");
        }
    }

    /// <summary>
    /// Checks if the tunnel graph contains circular dependencies using DFS.
    /// </summary>
    private bool HasCircularDependencies(TunnelProfile profile)
    {
        var visited = new HashSet<Guid>();
        var recursionStack = new HashSet<Guid>();

        foreach (var node in profile.Nodes)
        {
            if (HasCycleDfs(node.Id, profile, visited, recursionStack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// DFS helper for cycle detection.
    /// </summary>
    private bool HasCycleDfs(
        Guid nodeId,
        TunnelProfile profile,
        HashSet<Guid> visited,
        HashSet<Guid> recursionStack)
    {
        if (recursionStack.Contains(nodeId))
        {
            return true; // Cycle detected
        }

        if (visited.Contains(nodeId))
        {
            return false; // Already processed
        }

        visited.Add(nodeId);
        recursionStack.Add(nodeId);

        // Visit all neighbors
        var outgoingEdges = profile.Edges.Where(e => e.SourceNodeId == nodeId);
        foreach (var edge in outgoingEdges)
        {
            if (HasCycleDfs(edge.TargetNodeId, profile, visited, recursionStack))
            {
                return true;
            }
        }

        recursionStack.Remove(nodeId);
        return false;
    }

    /// <summary>
    /// Gets all nodes reachable from a given start node using BFS.
    /// </summary>
    private HashSet<Guid> GetReachableNodes(Guid startNodeId, TunnelProfile profile)
    {
        var reachable = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(startNodeId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (reachable.Contains(currentId))
            {
                continue;
            }

            reachable.Add(currentId);

            var outgoingEdges = profile.Edges.Where(e => e.SourceNodeId == currentId);
            foreach (var edge in outgoingEdges)
            {
                queue.Enqueue(edge.TargetNodeId);
            }
        }

        return reachable;
    }

    /// <summary>
    /// Builds the tunnel chain by finding the longest path through SSH hosts from the start node.
    /// For linear chains, returns nodes in connection order. For branched graphs, returns the primary path.
    /// </summary>
    /// <remarks>
    /// The algorithm finds the longest path through SshHost nodes to ensure proper proxy chain ordering.
    /// For example: LocalMachine → Jump1 → Jump2 → Target yields [LocalMachine, Jump1, Jump2, Target].
    /// Port forwarding and other node types are collected separately and don't affect path ordering.
    /// </remarks>
    private List<TunnelNode> BuildTunnelChain(TunnelProfile profile, TunnelNode startNode)
    {
        // Find the longest path through SSH hosts
        var longestPath = FindLongestSshHostPath(profile, startNode.Id);

        // If we found a path, use it as the primary chain
        if (longestPath.Count > 0)
        {
            var chain = new List<TunnelNode>();

            // Add nodes from the longest path
            foreach (var nodeId in longestPath)
            {
                var node = profile.Nodes.FirstOrDefault(n => n.Id == nodeId);
                if (node is not null)
                {
                    chain.Add(node);
                }
            }

            // Add any port forwarding or other special nodes that are connected but not in main path
            var chainNodeIds = new HashSet<Guid>(longestPath);
            var additionalNodes = profile.Nodes.Where(n =>
                !chainNodeIds.Contains(n.Id) &&
                (n.NodeType == TunnelNodeType.LocalPort ||
                 n.NodeType == TunnelNodeType.RemotePort ||
                 n.NodeType == TunnelNodeType.DynamicProxy ||
                 n.NodeType == TunnelNodeType.TargetHost)).ToList();

            chain.AddRange(additionalNodes);

            return chain;
        }

        // Fallback: return just the start node if no path found
        return [startNode];
    }

    /// <summary>
    /// Finds the longest path through SSH host nodes using DFS.
    /// This ensures proper ordering for multi-hop chains.
    /// </summary>
    private List<Guid> FindLongestSshHostPath(TunnelProfile profile, Guid startNodeId)
    {
        // Build node lookup dictionary for O(1) access
        var nodeLookup = profile.Nodes.ToDictionary(n => n.Id, n => n);

        var longestPath = new List<Guid>();
        var visited = new HashSet<Guid>();
        var currentPath = new List<Guid>();

        DfsLongestPath(profile, startNodeId, nodeLookup, visited, currentPath, ref longestPath);

        return longestPath;
    }

    /// <summary>
    /// DFS helper to find the longest path through the graph.
    /// Prioritizes paths with more SSH hosts for proper proxy chain ordering.
    /// </summary>
    private void DfsLongestPath(
        TunnelProfile profile,
        Guid currentNodeId,
        Dictionary<Guid, TunnelNode> nodeLookup,
        HashSet<Guid> visited,
        List<Guid> currentPath,
        ref List<Guid> longestPath)
    {
        visited.Add(currentNodeId);
        currentPath.Add(currentNodeId);

        // Count SSH hosts in current path for prioritization (using dictionary lookup)
        var sshHostCount = currentPath.Count(id =>
            nodeLookup.TryGetValue(id, out var node) && node.NodeType == TunnelNodeType.SshHost);

        // Count SSH hosts in longest path (using dictionary lookup)
        var longestSshHostCount = longestPath.Count(id =>
            nodeLookup.TryGetValue(id, out var node) && node.NodeType == TunnelNodeType.SshHost);

        // Update longest path if current path has more SSH hosts, or same SSH hosts but longer overall
        if (sshHostCount > longestSshHostCount ||
            (sshHostCount == longestSshHostCount && currentPath.Count > longestPath.Count))
        {
            longestPath = new List<Guid>(currentPath);
        }

        // Explore all outgoing edges
        var outgoingEdges = profile.Edges.Where(e => e.SourceNodeId == currentNodeId);
        foreach (var edge in outgoingEdges)
        {
            if (!visited.Contains(edge.TargetNodeId))
            {
                DfsLongestPath(profile, edge.TargetNodeId, nodeLookup, visited, currentPath, ref longestPath);
            }
        }

        // Backtrack
        currentPath.RemoveAt(currentPath.Count - 1);
        visited.Remove(currentNodeId);
    }

    /// <summary>
    /// Attempts to get the underlying SshClient from an ISshConnection.
    /// </summary>
    private SshClient? GetSshClientFromConnection(ISshConnection connection)
    {
        if (connection is SshConnectionBase baseConnection)
        {
            return baseConnection.GetSshClient();
        }

        _logger.LogWarning("Connection is not a SshConnectionBase instance");
        return null;
    }

    /// <summary>
    /// Validates that a port number is within the valid range (1-65535) before casting to uint.
    /// </summary>
    /// <param name="port">The port number to validate.</param>
    /// <param name="parameterName">The name of the parameter for error reporting.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when port is outside the valid range.</exception>
    /// <remarks>
    /// Security rationale: Port values are cast to uint throughout the tunnel builder.
    /// If a negative or out-of-range port value is provided, the cast could result in
    /// unexpected values or overflow. This validation ensures all ports are in the valid
    /// TCP/UDP port range (1-65535) before any cast operations, preventing potential
    /// security issues or unexpected behavior.
    /// </remarks>
    private static void ValidatePortNumber(int port, string parameterName)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                port,
                $"Port number must be between 1 and 65535. Got: {port}");
        }
    }

    /// <summary>
    /// Represents an active tunnel session with all associated resources.
    /// </summary>
    /// <remarks>
    /// Presence in the _activeTunnels dictionary indicates the tunnel is active.
    /// Thread-safe state management is ensured by using ConcurrentDictionary operations.
    /// </remarks>
    private sealed class ActiveTunnel
    {
        public required Guid ProfileId { get; init; }
        public required Guid SessionId { get; init; }
        public required string DisplayName { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public required ISshConnection Connection { get; init; }
        public required List<ForwardedPort> ForwardedPorts { get; init; }
    }
}
