using SshManager.Core.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Options for SSH config export.
/// </summary>
public class SshConfigExportOptions
{
    /// <summary>
    /// Include comments with group names and metadata.
    /// </summary>
    public bool IncludeComments { get; set; } = true;

    /// <summary>
    /// Include group name as section header comments.
    /// </summary>
    public bool IncludeGroups { get; set; } = true;

    /// <summary>
    /// Include port forwarding rules (LocalForward, RemoteForward, DynamicForward).
    /// </summary>
    public bool IncludePortForwarding { get; set; } = true;

    /// <summary>
    /// Use ProxyJump directive (modern) vs ProxyCommand (legacy).
    /// </summary>
    public bool UseProxyJump { get; set; } = true;
}

/// <summary>
/// Service for exporting hosts to OpenSSH config format.
/// </summary>
public interface ISshConfigExportService
{
    /// <summary>
    /// Generates SSH config content from hosts.
    /// </summary>
    string GenerateConfig(IEnumerable<HostEntry> hosts, SshConfigExportOptions options);

    /// <summary>
    /// Exports SSH config to a file.
    /// </summary>
    Task ExportToFileAsync(string filePath, IEnumerable<HostEntry> hosts, SshConfigExportOptions options, CancellationToken ct = default);
}
