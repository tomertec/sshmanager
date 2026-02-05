using System.IO.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for HostEntry entity.
/// </summary>
public sealed class HostEntryConfiguration : IEntityTypeConfiguration<HostEntry>
{
    public void Configure(EntityTypeBuilder<HostEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Hostname).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Username).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PrivateKeyPath).HasMaxLength(1000);
        builder.Property(x => x.Notes).HasMaxLength(5000); // Aligned with HostEntry.MaxNotesLength

        // Relationships
        builder.HasOne(x => x.Group)
            .WithMany(g => g.Hosts)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.HostProfile)
            .WithMany(p => p.Hosts)
            .HasForeignKey(x => x.HostProfileId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.ProxyJumpProfile)
            .WithMany(p => p.AssociatedHosts)
            .HasForeignKey(x => x.ProxyJumpProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes
        builder.HasIndex(x => x.DisplayName);
        builder.HasIndex(x => x.Hostname);
        builder.HasIndex(x => new { x.GroupId, x.SortOrder });

        // Connection and shell type configuration
        builder.Property(x => x.ConnectionType)
            .HasConversion<int>()
            .HasDefaultValue(ConnectionType.Ssh);
        builder.Property(x => x.ShellType)
            .HasConversion<int>()
            .HasDefaultValue(ShellType.Auto);

        // Serial port configuration
        builder.Property(x => x.SerialPortName);
        builder.Property(x => x.SerialBaudRate)
            .HasDefaultValue(9600);
        builder.Property(x => x.SerialDataBits)
            .HasDefaultValue(8);
        builder.Property(x => x.SerialStopBits)
            .HasConversion<int>()
            .HasDefaultValue(StopBits.One);
        builder.Property(x => x.SerialParity)
            .HasConversion<int>()
            .HasDefaultValue(Parity.None);
        builder.Property(x => x.SerialHandshake)
            .HasConversion<int>()
            .HasDefaultValue(Handshake.None);
        builder.Property(x => x.SerialDtrEnable)
            .HasDefaultValue(true);
        builder.Property(x => x.SerialRtsEnable)
            .HasDefaultValue(true);
        builder.Property(x => x.SerialLocalEcho)
            .HasDefaultValue(false);
        builder.Property(x => x.SerialLineEnding)
            .HasDefaultValue("\r\n");

        // Keep-alive configuration
        builder.Property(x => x.KeepAliveIntervalSeconds)
            .IsRequired(false);

        // Kerberos/GSSAPI authentication configuration
        builder.Property(x => x.KerberosServicePrincipal)
            .HasMaxLength(400);
        builder.Property(x => x.KerberosDelegateCredentials)
            .HasDefaultValue(false);
    }
}
