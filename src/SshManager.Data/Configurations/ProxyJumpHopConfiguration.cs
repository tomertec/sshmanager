using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for ProxyJumpHop entity (ordered junction table).
/// </summary>
public sealed class ProxyJumpHopConfiguration : IEntityTypeConfiguration<ProxyJumpHop>
{
    public void Configure(EntityTypeBuilder<ProxyJumpHop> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProxyJumpProfileId, x.SortOrder }).IsUnique();
        builder.HasOne(x => x.Profile)
            .WithMany(p => p.JumpHops)
            .HasForeignKey(x => x.ProxyJumpProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.JumpHost)
            .WithMany()
            .HasForeignKey(x => x.JumpHostId)
            .OnDelete(DeleteBehavior.Restrict); // Don't delete profile if host is deleted
    }
}
