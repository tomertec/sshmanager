using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for SavedSession entity (for crash recovery).
/// </summary>
public sealed class SavedSessionConfiguration : IEntityTypeConfiguration<SavedSession>
{
    public void Configure(EntityTypeBuilder<SavedSession> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.HostEntryId);
        builder.HasIndex(x => x.WasGracefulShutdown);
    }
}
