using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for parsing SSH config files (~/.ssh/config).
/// </summary>
public interface ISshConfigParser
{
    /// <summary>
    /// Parses an SSH config file and returns the parsed host entries.
    /// </summary>
    Task<SshConfigParseResult> ParseAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Gets the default SSH config path for the current user.
    /// </summary>
    string GetDefaultConfigPath();
}

/// <summary>
/// Result of parsing an SSH config file.
/// </summary>
public class SshConfigParseResult
{
    public List<SshConfigHost> Hosts { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Represents a parsed host entry from SSH config.
/// </summary>
public class SshConfigHost
{
    /// <summary>
    /// The Host alias from the config (e.g., "myserver").
    /// </summary>
    public string Alias { get; set; } = "";

    /// <summary>
    /// The actual hostname or IP (from HostName directive).
    /// </summary>
    public string? Hostname { get; set; }

    /// <summary>
    /// SSH port (from Port directive, default 22).
    /// </summary>
    public int Port { get; set; } = 22;

    /// <summary>
    /// Username (from User directive).
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Path to identity file (from IdentityFile directive).
    /// </summary>
    public string? IdentityFile { get; set; }

    /// <summary>
    /// ProxyJump directive value - can be a single host alias or comma-separated chain.
    /// Example: "bastion" or "bastion1,bastion2"
    /// </summary>
    public string? ProxyJump { get; set; }

    /// <summary>
    /// Local port forwarding entries (from LocalForward directives).
    /// </summary>
    public List<LocalForwardEntry> LocalForwards { get; set; } = [];

    /// <summary>
    /// Remote port forwarding entries (from RemoteForward directives).
    /// </summary>
    public List<RemoteForwardEntry> RemoteForwards { get; set; } = [];

    /// <summary>
    /// Dynamic port forwarding entries - SOCKS proxy ports (from DynamicForward directives).
    /// </summary>
    public List<DynamicForwardEntry> DynamicForwards { get; set; } = [];

    /// <summary>
    /// Converts this SSH config host to a HostEntry.
    /// </summary>
    public HostEntry ToHostEntry()
    {
        var authType = !string.IsNullOrEmpty(IdentityFile)
            ? AuthType.PrivateKeyFile
            : AuthType.SshAgent;

        return new HostEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = Alias,
            Hostname = Hostname ?? Alias,
            Port = Port,
            Username = User ?? Environment.UserName,
            AuthType = authType,
            PrivateKeyPath = IdentityFile,
            Notes = $"Imported from SSH config",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Checks if this host has any ProxyJump or port forwarding configuration.
    /// </summary>
    public bool HasAdvancedConfig =>
        !string.IsNullOrEmpty(ProxyJump) ||
        LocalForwards.Count > 0 ||
        RemoteForwards.Count > 0 ||
        DynamicForwards.Count > 0;
}

/// <summary>
/// Represents a LocalForward directive entry.
/// Format: [bind_address:]port host:hostport
/// </summary>
/// <param name="BindAddress">Local bind address (default: 127.0.0.1).</param>
/// <param name="LocalPort">Local port to bind.</param>
/// <param name="RemoteHost">Remote host to forward to.</param>
/// <param name="RemotePort">Remote port to forward to.</param>
public record LocalForwardEntry(
    string BindAddress,
    int LocalPort,
    string RemoteHost,
    int RemotePort);

/// <summary>
/// Represents a RemoteForward directive entry.
/// Format: [bind_address:]port host:hostport
/// </summary>
/// <param name="BindAddress">Remote bind address (default: localhost on remote).</param>
/// <param name="RemotePort">Remote port to bind on the server.</param>
/// <param name="LocalHost">Local host to forward traffic to.</param>
/// <param name="LocalPort">Local port to forward traffic to.</param>
public record RemoteForwardEntry(
    string BindAddress,
    int RemotePort,
    string LocalHost,
    int LocalPort);

/// <summary>
/// Represents a DynamicForward directive entry (SOCKS proxy).
/// Format: [bind_address:]port
/// </summary>
/// <param name="BindAddress">Local bind address (default: 127.0.0.1).</param>
/// <param name="Port">Local port for the SOCKS proxy.</param>
public record DynamicForwardEntry(
    string BindAddress,
    int Port);

/// <summary>
/// Represents import data for a single host with all its advanced configuration.
/// </summary>
public class SshConfigImportItem
{
    /// <summary>
    /// The host entry to import.
    /// </summary>
    public required HostEntry HostEntry { get; init; }

    /// <summary>
    /// The original SSH config host with advanced configuration.
    /// </summary>
    public required SshConfigHost ConfigHost { get; init; }

    /// <summary>
    /// Whether this host has ProxyJump or port forwarding configuration.
    /// </summary>
    public bool HasAdvancedConfig => ConfigHost.HasAdvancedConfig;
}
