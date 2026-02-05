using Microsoft.Extensions.DependencyInjection;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Main service registrar that composes all feature modules for DI configuration.
/// </summary>
public static class ServiceRegistrar
{
    /// <summary>
    /// Registers all SshManager services using feature modules for clearer composition.
    /// </summary>
    /// <remarks>
    /// Registration order:
    /// 1. Data - Database context, repositories, data services
    /// 2. Security - Encryption, credentials, SSH key management
    /// 3. Terminal - SSH connections, sessions, terminal controls
    /// 4. App - WPF-UI, app services, ViewModels, Windows
    /// 5. HostedServices - Startup tasks, background services
    /// </remarks>
    public static IServiceCollection AddSshManagerServices(this IServiceCollection services)
    {
        return services
            .AddDataServices()
            .AddSecurityServices()
            .AddTerminalServices()
            .AddAppServices()
            .AddHostedServices();
    }
}
