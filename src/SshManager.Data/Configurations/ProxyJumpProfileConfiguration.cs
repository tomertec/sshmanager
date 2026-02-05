using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for ProxyJumpProfile entity.
/// </summary>
public sealed class ProxyJumpProfileConfiguration : IEntityTypeConfiguration<ProxyJumpProfile>
{
    public void Configure(EntityTypeBuilder<ProxyJumpProfile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.DisplayName);
    }
}
