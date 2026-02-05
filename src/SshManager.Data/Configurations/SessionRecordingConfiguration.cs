using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for SessionRecording entity.
/// </summary>
public sealed class SessionRecordingConfiguration : IEntityTypeConfiguration<SessionRecording>
{
    public void Configure(EntityTypeBuilder<SessionRecording> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasOne(x => x.Host)
            .WithMany()
            .HasForeignKey(x => x.HostId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.StartedAt);
        builder.HasIndex(x => x.HostId);
        builder.HasIndex(x => x.IsArchived);
    }
}
