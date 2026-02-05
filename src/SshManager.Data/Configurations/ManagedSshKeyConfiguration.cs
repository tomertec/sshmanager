using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for ManagedSshKey entity.
/// </summary>
public sealed class ManagedSshKeyConfiguration : IEntityTypeConfiguration<ManagedSshKey>
{
    public void Configure(EntityTypeBuilder<ManagedSshKey> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PrivateKeyPath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => x.PrivateKeyPath).IsUnique();
        builder.HasIndex(x => x.DisplayName);
    }
}
