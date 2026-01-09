using SshManager.Core.Models;

namespace SshManager.App.Services;

public interface IExportImportService
{
    Task ExportAsync(string filePath, IEnumerable<HostEntry> hosts, IEnumerable<HostGroup> groups, CancellationToken ct = default);
    Task<(List<HostEntry> Hosts, List<HostGroup> Groups)> ImportAsync(string filePath, CancellationToken ct = default);
}
