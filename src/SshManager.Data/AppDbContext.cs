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

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // HostEntry configuration
        modelBuilder.Entity<HostEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Hostname).HasMaxLength(400).IsRequired();
            e.Property(x => x.Username).HasMaxLength(200).IsRequired();
            e.Property(x => x.PrivateKeyPath).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasOne(x => x.Group)
             .WithMany(g => g.Hosts)
             .HasForeignKey(x => x.GroupId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.HostProfile)
             .WithMany(p => p.Hosts)
             .HasForeignKey(x => x.HostProfileId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ProxyJumpProfile)
             .WithMany(p => p.AssociatedHosts)
             .HasForeignKey(x => x.ProxyJumpProfileId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.DisplayName);
            e.HasIndex(x => x.Hostname);
            e.HasIndex(x => new { x.GroupId, x.SortOrder });
        });

        // HostGroup configuration
        modelBuilder.Entity<HostGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Icon).HasMaxLength(50);
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => x.SortOrder);
        });

        // ConnectionHistory configuration
        modelBuilder.Entity<ConnectionHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Host)
             .WithMany()
             .HasForeignKey(x => x.HostId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.HasIndex(x => x.ConnectedAt);
        });

        // AppSettings configuration
        modelBuilder.Entity<AppSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TerminalFontFamily).HasMaxLength(100);
        });

        // CommandSnippet configuration
        modelBuilder.Entity<CommandSnippet>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Command).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Category).HasMaxLength(100);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.SortOrder);
        });

        // HostFingerprint configuration
        modelBuilder.Entity<HostFingerprint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Algorithm).HasMaxLength(100).IsRequired();
            e.Property(x => x.Fingerprint).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Host)
             .WithMany()
             .HasForeignKey(x => x.HostId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.HostId).IsUnique();
        });

        // ManagedSshKey configuration
        modelBuilder.Entity<ManagedSshKey>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PrivateKeyPath).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Fingerprint).HasMaxLength(100).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(500);
            e.HasIndex(x => x.PrivateKeyPath).IsUnique();
            e.HasIndex(x => x.DisplayName);
        });

        // ProxyJumpProfile configuration
        modelBuilder.Entity<ProxyJumpProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.DisplayName);
        });

        // ProxyJumpHop configuration (ordered junction table)
        modelBuilder.Entity<ProxyJumpHop>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProxyJumpProfileId, x.SortOrder }).IsUnique();
            e.HasOne(x => x.Profile)
             .WithMany(p => p.JumpHops)
             .HasForeignKey(x => x.ProxyJumpProfileId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JumpHost)
             .WithMany()
             .HasForeignKey(x => x.JumpHostId)
             .OnDelete(DeleteBehavior.Restrict); // Don't delete profile if host is deleted
        });

        // PortForwardingProfile configuration
        modelBuilder.Entity<PortForwardingProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.LocalBindAddress).HasMaxLength(400).IsRequired();
            e.Property(x => x.RemoteHost).HasMaxLength(400);
            e.HasOne(x => x.Host)
             .WithMany(h => h.PortForwardingProfiles)
             .HasForeignKey(x => x.HostId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DisplayName);
            e.HasIndex(x => x.HostId);
        });

        // HostProfile configuration
        modelBuilder.Entity<HostProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.DefaultUsername).HasMaxLength(100);
            e.Property(x => x.PrivateKeyPath).HasMaxLength(1000);
            e.HasOne(x => x.ProxyJumpProfile)
             .WithMany()
             .HasForeignKey(x => x.ProxyJumpProfileId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.DisplayName);
        });
    }
}
