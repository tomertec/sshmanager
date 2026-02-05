using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for CommandHistoryEntry entity.
/// </summary>
public sealed class CommandHistoryEntryConfiguration : IEntityTypeConfiguration<CommandHistoryEntry>
{
    public void Configure(EntityTypeBuilder<CommandHistoryEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Command).HasMaxLength(4000).IsRequired();
        builder.HasIndex(x => x.HostId);
        builder.HasIndex(x => new { x.HostId, x.Command }).IsUnique();
        builder.HasIndex(x => x.ExecutedAt);
    }
}
