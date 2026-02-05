using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for ConnectionHistory entity.
/// </summary>
public sealed class ConnectionHistoryConfiguration : IEntityTypeConfiguration<ConnectionHistory>
{
    public void Configure(EntityTypeBuilder<ConnectionHistory> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.Host)
            .WithMany()
            .HasForeignKey(x => x.HostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.HasIndex(x => x.ConnectedAt);
    }
}
