using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for TunnelEdge entity.
/// </summary>
public sealed class TunnelEdgeConfiguration : IEntityTypeConfiguration<TunnelEdge>
{
    public void Configure(EntityTypeBuilder<TunnelEdge> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.TunnelProfile)
            .WithMany(p => p.Edges)
            .HasForeignKey(x => x.TunnelProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SourceNode)
            .WithMany()
            .HasForeignKey(x => x.SourceNodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetNode)
            .WithMany()
            .HasForeignKey(x => x.TargetNodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.TunnelProfileId);
        builder.HasIndex(x => new { x.SourceNodeId, x.TargetNodeId });
    }
}
