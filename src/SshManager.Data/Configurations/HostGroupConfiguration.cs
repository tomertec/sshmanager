using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for HostGroup entity.
/// </summary>
public sealed class HostGroupConfiguration : IEntityTypeConfiguration<HostGroup>
{
    public void Configure(EntityTypeBuilder<HostGroup> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Icon).HasMaxLength(50);
        builder.Property(x => x.Color).HasMaxLength(20);
        builder.HasIndex(x => x.SortOrder);
    }
}
