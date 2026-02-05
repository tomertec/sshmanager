using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for TunnelNode entity.
/// </summary>
public sealed class TunnelNodeConfiguration : IEntityTypeConfiguration<TunnelNode>
{
    public void Configure(EntityTypeBuilder<TunnelNode> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RemoteHost).HasMaxLength(400);
        builder.Property(x => x.BindAddress).HasMaxLength(100);
        builder.Property(x => x.NodeType).HasConversion<int>();
        builder.HasOne(x => x.TunnelProfile)
            .WithMany(p => p.Nodes)
            .HasForeignKey(x => x.TunnelProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.TunnelProfileId);
    }
}
