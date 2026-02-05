using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for TunnelProfile entity.
/// </summary>
public sealed class TunnelProfileConfiguration : IEntityTypeConfiguration<TunnelProfile>
{
    public void Configure(EntityTypeBuilder<TunnelProfile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.DisplayName);
    }
}
