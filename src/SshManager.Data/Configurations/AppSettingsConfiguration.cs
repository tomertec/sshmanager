using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SshManager.Core.Models;

namespace SshManager.Data.Configurations;

/// <summary>
/// EF Core configuration for AppSettings entity.
/// </summary>
public sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TerminalFontFamily).HasMaxLength(100);
    }
}
