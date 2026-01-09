using System.IO.Ports;
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
            e.Property(x => x.Notes).HasMaxLength(5000); // Aligned with HostEntry.MaxNotesLength
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

            // Connection and shell type configuration
            e.Property(x => x.ConnectionType)
                .HasConversion<int>()
                .HasDefaultValue(ConnectionType.Ssh);
            e.Property(x => x.ShellType)
                .HasConversion<int>()
                .HasDefaultValue(ShellType.Auto);

            // Serial port configuration
            e.Property(x => x.SerialPortName);
            e.Property(x => x.SerialBaudRate)
                .HasDefaultValue(9600);
            e.Property(x => x.SerialDataBits)
                .HasDefaultValue(8);
            e.Property(x => x.SerialStopBits)
                .HasConversion<int>()
                .HasDefaultValue(StopBits.One);
            e.Property(x => x.SerialParity)
                .HasConversion<int>()
                .HasDefaultValue(Parity.None);
            e.Property(x => x.SerialHandshake)
                .HasConversion<int>()
                .HasDefaultValue(Handshake.None);
            e.Property(x => x.SerialDtrEnable)
                .HasDefaultValue(true);
            e.Property(x => x.SerialRtsEnable)
                .HasDefaultValue(true);
            e.Property(x => x.SerialLocalEcho)
                .HasDefaultValue(false);
            e.Property(x => x.SerialLineEnding)
                .HasDefaultValue("\r\n");

            // Keep-alive configuration
            e.Property(x => x.KeepAliveIntervalSeconds)
                .IsRequired(false);
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
        // Supports multiple key algorithms per host (RSA, ED25519, ECDSA, etc.)
        modelBuilder.Entity<HostFingerprint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Algorithm).HasMaxLength(100).IsRequired();
            e.Property(x => x.Fingerprint).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Host)
             .WithMany()
             .HasForeignKey(x => x.HostId)
             .OnDelete(DeleteBehavior.Cascade);
            // Composite unique index: one fingerprint per (host, algorithm) pair
            // Allows storing multiple key types (RSA, ED25519, ECDSA) for the same host
            e.HasIndex(x => new { x.HostId, x.Algorithm }).IsUnique();
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

        // Tag configuration
        modelBuilder.Entity<Tag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Color).HasMaxLength(7);
            e.HasIndex(x => x.Name);
            e.HasMany(x => x.Hosts)
             .WithMany(h => h.Tags);
        });

        // HostEnvironmentVariable configuration
        modelBuilder.Entity<HostEnvironmentVariable>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4096);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne(x => x.Host)
             .WithMany(h => h.EnvironmentVariables)
             .HasForeignKey(x => x.HostEntryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HostEntryId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.HostEntryId, x.SortOrder });
        });

        // SessionRecording configuration
        modelBuilder.Entity<SessionRecording>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.Host)
             .WithMany()
             .HasForeignKey(x => x.HostId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.HostId);
            e.HasIndex(x => x.IsArchived);
        });
    }
}
