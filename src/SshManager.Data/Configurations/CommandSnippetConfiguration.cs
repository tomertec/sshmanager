using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for CommandSnippet entity.
/// </summary>
public sealed class CommandSnippetConfiguration : IEntityTypeConfiguration<CommandSnippet>
{
    public void Configure(EntityTypeBuilder<CommandSnippet> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Command).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.SortOrder);
    }
}
