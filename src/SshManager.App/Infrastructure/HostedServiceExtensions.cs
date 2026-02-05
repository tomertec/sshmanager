using Microsoft.Extensions.DependencyInjection;
using SshManager.App.Services;
using SshManager.App.Services.Hosting;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Extension methods for registering hosted services (startup tasks, background services).
/// </summary>
public static class HostedServiceExtensions
{
    public static IServiceCollection AddHostedServices(this IServiceCollection services)
    {
        // Register CloudSyncHostedService and AutoBackupHostedService as singletons first
        // so they can be resolved by IBackgroundServiceHealth
        services.AddSingleton<CloudSyncHostedService>();
        services.AddSingleton<AutoBackupHostedService>();

        // Startup hosted services (order matters - database must be first)
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHostedService<ThemeInitializationHostedService>();
        services.AddHostedService<SystemTrayHostedService>();
        services.AddHostedService<CredentialCacheHostedService>();
        services.AddHostedService<StartupTasksHostedService>();

        // Background services (resolved from singleton registrations above)
        services.AddHostedService(sp => sp.GetRequiredService<AutoBackupHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<CloudSyncHostedService>());
        services.AddHostedService<HostStatusHostedService>();

        // Background service health aggregation
        services.AddSingleton<IBackgroundServiceHealth>(sp => sp.GetRequiredService<AutoBackupHostedService>());
        services.AddSingleton<IBackgroundServiceHealthAggregator, BackgroundServiceHealthAggregator>();

        return services;
    }
}
