using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for HostProfile entity.
/// </summary>
public sealed class HostProfileConfiguration : IEntityTypeConfiguration<HostProfile>
{
    public void Configure(EntityTypeBuilder<HostProfile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.DefaultUsername).HasMaxLength(100);
        builder.Property(x => x.PrivateKeyPath).HasMaxLength(1000);
        builder.HasOne(x => x.ProxyJumpProfile)
            .WithMany()
            .HasForeignKey(x => x.ProxyJumpProfileId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.DisplayName);
    }
}
