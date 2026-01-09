using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for application settings.
/// </summary>
public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(AppSettings settings, CancellationToken ct = default);
}
