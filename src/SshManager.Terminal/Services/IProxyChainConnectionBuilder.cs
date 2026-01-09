using Renci.SshNet;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Builder interface for establishing proxy chain SSH connections.
/// </summary>
public interface IProxyChainConnectionBuilder
{
    /// <summary>
    /// Builds a proxy chain connection through multiple hops.
    /// </summary>
    /// <param name="connectionChain">
    /// Ordered list of connection info for each hop, ending with the target host.
    /// The first entry is the first jump host (directly reachable).
    /// The last entry is the final target host.
    /// </param>
    /// <param name="hostKeyCallback">Callback for verifying host keys at each hop.</param>
    /// <param name="kbInteractiveCallback">Callback for keyboard-interactive auth at each hop.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Build result containing all established clients and port forwards.</returns>
    Task<ProxyChainBuildResult> BuildChainAsync(
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken ct);
}

/// <summary>
/// Result of building a proxy chain connection.
/// Contains all intermediate clients and port forwards needed for the chain.
/// </summary>
/// <param name="TargetClient">The SSH client connected to the final target host.</param>
/// <param name="FinalLocalPort">The local port through which the target is accessible.</param>
/// <param name="IntermediateClients">The intermediate SSH clients in the proxy chain.</param>
/// <param name="ForwardedPorts">The local port forwards used for tunneling.</param>
/// <param name="Disposables">Disposable resources (e.g., PrivateKeyFile) that need cleanup.</param>
public record ProxyChainBuildResult(
    SshClient TargetClient,
    int FinalLocalPort,
    IReadOnlyList<SshClient> IntermediateClients,
    IReadOnlyList<ForwardedPortLocal> ForwardedPorts,
    IReadOnlyList<IDisposable> Disposables);
