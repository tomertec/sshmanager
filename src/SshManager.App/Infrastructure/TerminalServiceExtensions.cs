using Microsoft.Extensions.DependencyInjection;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using SshManager.Terminal.Services.Playback;
using SshManager.Terminal.Services.Recording;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Extension methods for registering terminal services (SSH connections, sessions, terminal controls).
/// </summary>
public static class TerminalServiceExtensions
{
    public static IServiceCollection AddTerminalServices(this IServiceCollection services)
    {
        // Core terminal services
        services.AddSingleton<ITerminalResizeService, TerminalResizeService>();
        services.AddSingleton<IConnectionRetryPolicy, ConnectionRetryPolicy>();
        services.AddSingleton<ISshAuthenticationFactory, SshAuthenticationFactory>();
        services.AddSingleton<IAgentDiagnosticsService, AgentDiagnosticsService>();
        services.AddSingleton<IAgentKeyService, AgentKeyService>();
        services.AddSingleton<IKerberosAuthService, KerberosAuthService>();
        services.AddSingleton<IProxyChainConnectionBuilder, ProxyChainConnectionBuilder>();
        services.AddSingleton<ISshConnectionService, SshConnectionService>();
        services.AddSingleton<ISftpService, SftpService>();
        services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();
        services.AddSingleton<ISessionLoggingService, SessionLoggingService>();
        services.AddSingleton<IBroadcastInputService, BroadcastInputService>();
        services.AddSingleton<IServerStatsService, ServerStatsService>();
        services.AddSingleton<IProxyJumpService, ProxyJumpService>();
        services.AddSingleton<IPortForwardingService, PortForwardingService>();
        services.AddSingleton<ITerminalThemeService, TerminalThemeService>();
        services.AddSingleton<ITerminalFocusTracker, TerminalFocusTracker>();
        services.AddSingleton<ISessionRecordingService, SessionRecordingService>();
        services.AddSingleton<ISessionPlaybackService, SessionPlaybackService>();
        services.AddSingleton<ISerialConnectionService, SerialConnectionService>();
        services.AddSingleton<IConnectionPool, ConnectionPool>();
        services.AddSingleton<IX11ForwardingService, X11ForwardingService>();
        services.AddSingleton<ITunnelBuilderService, TunnelBuilderService>();
        services.AddSingleton<IAutocompletionService, AutocompletionService>();
        services.AddSingleton<ISshConfigExportService, SshConfigExportService>();

        // Terminal control extracted services (transient - per-control instances)
        services.AddTransient<ITerminalClipboardService, TerminalClipboardService>();
        services.AddTransient<ITerminalKeyboardHandler, TerminalKeyboardHandler>();
        services.AddTransient<ITerminalConnectionHandler, TerminalConnectionHandler>();

        return services;
    }
}
