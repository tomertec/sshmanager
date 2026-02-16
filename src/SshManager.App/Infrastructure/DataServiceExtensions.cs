using Microsoft.Data.Sqlite;
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
        // Database context factory with WAL mode and busy timeout for concurrent access safety
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            var dbPath = DbPaths.GetDbPath();
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                // busy_timeout prevents "database is locked" errors under concurrent access
                DefaultTimeout = 5
            }.ToString();

            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });
        });

        // Execute WAL mode pragma once at startup via a shared connection.
        // WAL mode persists across connections once set, so we only need to do this once.
        {
            var dbPath = DbPaths.GetDbPath();
            var pragmaConnection = new SqliteConnection($"Data Source={dbPath}");
            try
            {
                pragmaConnection.Open();
                using var walCmd = pragmaConnection.CreateCommand();
                walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                walCmd.ExecuteNonQuery();
                using var busyCmd = pragmaConnection.CreateCommand();
                busyCmd.CommandText = "PRAGMA busy_timeout=5000;";
                busyCmd.ExecuteNonQuery();
            }
            finally
            {
                pragmaConnection.Close();
                pragmaConnection.Dispose();
            }
        }

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
