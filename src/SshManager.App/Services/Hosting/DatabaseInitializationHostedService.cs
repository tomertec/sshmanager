using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SshManager.App.Infrastructure;
using SshManager.Core.Models;
using SshManager.Data;
using SshManager.Data.Repositories;

namespace SshManager.App.Services.Hosting;

/// <summary>
/// Hosted service that initializes the database on application startup.
/// Handles database creation, schema migrations, and initial data seeding.
/// </summary>
/// <remarks>
/// This service implements <see cref="IHostedService"/> (not BackgroundService) because
/// database initialization must complete before other services can start.
/// </remarks>
public class DatabaseInitializationHostedService : IHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHostRepository _hostRepo;
    private readonly ILogger<DatabaseInitializationHostedService> _logger;

    public DatabaseInitializationHostedService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHostRepository hostRepo,
        ILogger<DatabaseInitializationHostedService> logger)
    {
        _dbFactory = dbFactory;
        _hostRepo = hostRepo;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the database, applies migrations, and seeds initial data.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing database...");

        try
        {
            // Ensure database is created
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            _logger.LogDebug("Database initialized at {DbPath}", DbPaths.GetDbPath());

            // Apply WAL mode and busy_timeout pragmas once after the database file exists.
            // WAL mode persists across connections once set, so we only need to do this once.
            // Doing this here (rather than during DI registration) ensures the DB file exists first.
            await ApplyWalPragmasAsync(cancellationToken);

            // Apply schema migrations for new columns
            // Note: DbMigrator uses Serilog.ILogger directly
            var serilogLogger = Log.ForContext<DatabaseInitializationHostedService>();
            await DbMigrator.MigrateAsync(db, serilogLogger);

            // Seed sample host if database is empty
            await SeedSampleHostAsync(cancellationToken);

            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    /// <summary>
    /// No cleanup required on shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies WAL journal mode and busy_timeout pragmas via a dedicated connection.
    /// WAL mode reduces write contention for the factory's concurrent connections.
    /// </summary>
    private async Task ApplyWalPragmasAsync(CancellationToken cancellationToken)
    {
        var dbPath = DbPaths.GetDbPath();
        await using var pragmaConnection = new SqliteConnection($"Data Source={dbPath}");
        await pragmaConnection.OpenAsync(cancellationToken);

        await using var walCmd = pragmaConnection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await walCmd.ExecuteNonQueryAsync(cancellationToken);

        await using var busyCmd = pragmaConnection.CreateCommand();
        busyCmd.CommandText = "PRAGMA busy_timeout=5000;";
        await busyCmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Applied WAL journal mode and busy_timeout pragmas");
    }

    /// <summary>
    /// Seeds a sample host entry if the database is empty (first-time user experience).
    /// </summary>
    private async Task SeedSampleHostAsync(CancellationToken cancellationToken)
    {
        var hasHosts = await _hostRepo.AnyAsync(cancellationToken);
        if (!hasHosts)
        {
            await _hostRepo.AddAsync(new HostEntry
            {
                DisplayName = "Sample Host",
                Hostname = "localhost",
                Username = Environment.UserName,
                Port = 22,
                AuthType = AuthType.SshAgent,
                Notes = "This is a sample host. Edit or delete it."
            }, cancellationToken);
            _logger.LogInformation("Created sample host entry for first-time user");
        }
    }
}
