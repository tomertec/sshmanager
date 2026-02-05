using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SshManager.Data;
using SshManager.Data.Repositories;
using SshManager.Data.Services;

namespace SshManager.App.Infrastructure;

/// <summary>
/// Extension methods for registering data layer services (EF Core, repositories, data services).
/// </summary>
public static class DataServiceExtensions
{
    public static IServiceCollection AddDataServices(this IServiceCollection services)
    {
        // Database context factory
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            var dbPath = DbPaths.GetDbPath();
            options.UseSqlite($"Data Source={dbPath}");
        });

        // Repositories
        services.AddSingleton<IHostRepository, HostRepository>();
        services.AddSingleton<IGroupRepository, GroupRepository>();
        services.AddSingleton<IHostProfileRepository, HostProfileRepository>();
        services.AddSingleton<IConnectionHistoryRepository, ConnectionHistoryRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ISnippetRepository, SnippetRepository>();
        services.AddSingleton<IHostFingerprintRepository, HostFingerprintRepository>();
        services.AddSingleton<IManagedKeyRepository, ManagedKeyRepository>();
        services.AddSingleton<IProxyJumpProfileRepository, ProxyJumpProfileRepository>();
        services.AddSingleton<IPortForwardingProfileRepository, PortForwardingProfileRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<IHostEnvironmentVariableRepository, HostEnvironmentVariableRepository>();
        services.AddSingleton<ISessionRecordingRepository, SessionRecordingRepository>();
        services.AddSingleton<ISavedSessionRepository, SavedSessionRepository>();
        services.AddSingleton<ITunnelProfileRepository, TunnelProfileRepository>();
        services.AddSingleton<ICommandHistoryRepository, CommandHistoryRepository>();

        // Data services
        services.AddSingleton<IConnectionHistoryCleanupService, ConnectionHistoryCleanupService>();
        services.AddSingleton<IHostCacheService, HostCacheService>();

        return services;
    }
}
