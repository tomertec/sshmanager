using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for PortForwardingProfile entity.
/// </summary>
public sealed class PortForwardingProfileConfiguration : IEntityTypeConfiguration<PortForwardingProfile>
{
    public void Configure(EntityTypeBuilder<PortForwardingProfile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.LocalBindAddress).HasMaxLength(400).IsRequired();
        builder.Property(x => x.RemoteHost).HasMaxLength(400);
        builder.HasOne(x => x.Host)
            .WithMany(h => h.PortForwardingProfiles)
            .HasForeignKey(x => x.HostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.DisplayName);
        builder.HasIndex(x => x.HostId);
    }
}
