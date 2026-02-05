using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for HostFingerprint entity.
/// Supports multiple key algorithms per host (RSA, ED25519, ECDSA, etc.)
/// </summary>
public sealed class HostFingerprintConfiguration : IEntityTypeConfiguration<HostFingerprint>
{
    public void Configure(EntityTypeBuilder<HostFingerprint> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Algorithm).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(500).IsRequired();
        builder.HasOne(x => x.Host)
            .WithMany()
            .HasForeignKey(x => x.HostId)
            .OnDelete(DeleteBehavior.Cascade);
        // Composite unique index: one fingerprint per (host, algorithm) pair
        // Allows storing multiple key types (RSA, ED25519, ECDSA) for the same host
        builder.HasIndex(x => new { x.HostId, x.Algorithm }).IsUnique();
    }
}
