using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data;

/// <summary>
/// Entity Framework Core database context for SshManager.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public DbSet<HostEntry> Hosts => Set<HostEntry>();
    public DbSet<HostGroup> Groups => Set<HostGroup>();
    public DbSet<HostProfile> HostProfiles => Set<HostProfile>();
    public DbSet<ConnectionHistory> ConnectionHistory => Set<ConnectionHistory>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<CommandSnippet> Snippets => Set<CommandSnippet>();
    public DbSet<HostFingerprint> HostFingerprints => Set<HostFingerprint>();
    public DbSet<ManagedSshKey> ManagedSshKeys => Set<ManagedSshKey>();
    public DbSet<ProxyJumpProfile> ProxyJumpProfiles => Set<ProxyJumpProfile>();
    public DbSet<ProxyJumpHop> ProxyJumpHops => Set<ProxyJumpHop>();
    public DbSet<PortForwardingProfile> PortForwardingProfiles => Set<PortForwardingProfile>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<HostEnvironmentVariable> HostEnvironmentVariables => Set<HostEnvironmentVariable>();
    public DbSet<SessionRecording> SessionRecordings => Set<SessionRecording>();
    public DbSet<SavedSession> SavedSessions => Set<SavedSession>();
    public DbSet<TunnelProfile> TunnelProfiles => Set<TunnelProfile>();
    public DbSet<TunnelNode> TunnelNodes => Set<TunnelNode>();
    public DbSet<TunnelEdge> TunnelEdges => Set<TunnelEdge>();
    public DbSet<CommandHistoryEntry> CommandHistory => Set<CommandHistoryEntry>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all IEntityTypeConfiguration<T> implementations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
