using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for HostEnvironmentVariable entity.
/// </summary>
public sealed class HostEnvironmentVariableConfiguration : IEntityTypeConfiguration<HostEnvironmentVariable>
{
    public void Configure(EntityTypeBuilder<HostEnvironmentVariable> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(4096);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasOne(x => x.Host)
            .WithMany(h => h.EnvironmentVariables)
            .HasForeignKey(x => x.HostEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.HostEntryId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.HostEntryId, x.SortOrder });
    }
}
