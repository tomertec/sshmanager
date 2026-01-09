using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SshManager.Core.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for exporting SSH host configurations to OpenSSH config format.
/// </summary>
public sealed partial class SshConfigExportService : ISshConfigExportService
{
    /// <summary>
    /// Generates OpenSSH config format from a collection of host entries.
    /// </summary>
    /// <param name="hosts">The host entries to export.</param>
    /// <param name="options">Export options.</param>
    /// <returns>The generated SSH config content.</returns>
    public string GenerateConfig(IEnumerable<HostEntry> hosts, SshConfigExportOptions? options = null)
    {
        options ??= new SshConfigExportOptions();
        var sb = new StringBuilder();

        // Add header comment
        if (options.IncludeComments)
        {
            sb.AppendLine("# SSH Config exported from SshManager");
            sb.AppendLine($"# Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
        }

        // Group hosts by their group
        var groupedHosts = hosts
            .OrderBy(h => h.Group?.Name ?? "")
            .ThenBy(h => h.SortOrder)
            .ThenBy(h => h.DisplayName)
            .GroupBy(h => h.Group?.Name ?? "Ungrouped");

        bool firstGroup = true;
        foreach (var group in groupedHosts)
        {
            // Add spacing between groups
            if (!firstGroup)
            {
                sb.AppendLine();
            }
            firstGroup = false;

            // Add group header comment
            if (options.IncludeGroups)
            {
                sb.AppendLine($"# Group: {group.Key}");
            }

            // Generate config for each host in the group
            foreach (var host in group)
            {
                GenerateHostConfig(sb, host, options);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates SSH config for a single host entry.
    /// </summary>
    /// <param name="sb">The StringBuilder to append to.</param>
    /// <param name="host">The host entry to generate config for.</param>
    /// <param name="options">Export options.</param>
    private void GenerateHostConfig(StringBuilder sb, HostEntry host, SshConfigExportOptions options)
    {
        // Generate sanitized host alias
        string alias = SanitizeHostAlias(host.DisplayName);

        sb.AppendLine($"Host {alias}");
        sb.AppendLine($"    HostName {host.Hostname}");

        // Only include port if not default (22)
        if (host.Port != 22)
        {
            sb.AppendLine($"    Port {host.Port}");
        }

        // Include username if specified
        if (!string.IsNullOrWhiteSpace(host.Username))
        {
            sb.AppendLine($"    User {host.Username}");
        }

        // Handle authentication based on type
        switch (host.AuthType)
        {
            case AuthType.PrivateKeyFile:
                if (!string.IsNullOrWhiteSpace(host.PrivateKeyPath))
                {
                    sb.AppendLine($"    IdentityFile {host.PrivateKeyPath}");
                }
                break;

            case AuthType.Password:
                sb.AppendLine("    # Password authentication - enter manually");
                break;

            case AuthType.SshAgent:
                // No IdentityFile line needed - will use SSH agent
                break;
        }

        // Handle ProxyJump if configured
        if (options.UseProxyJump &&
            host.ProxyJumpProfile?.IsEnabled == true &&
            host.ProxyJumpProfile.JumpHops.Any())
        {
            var jumpHosts = host.ProxyJumpProfile.JumpHops
                .OrderBy(h => h.SortOrder)
                .Select(h => h.JumpHost)
                .Where(h => h != null)
                .Select(h => SanitizeHostAlias(h!.DisplayName))
                .ToList();

            if (jumpHosts.Count > 0)
            {
                string proxyJumpValue = string.Join(",", jumpHosts);
                sb.AppendLine($"    ProxyJump {proxyJumpValue}");
            }
        }

        // Handle port forwarding if configured
        if (options.IncludePortForwarding && host.PortForwardingProfiles.Any())
        {
            var enabledProfiles = host.PortForwardingProfiles
                .Where(p => p.IsEnabled)
                .ToList();

            foreach (var profile in enabledProfiles)
            {
                GeneratePortForwardingConfig(sb, profile);
            }
        }

        // Add notes as comments if present
        if (!string.IsNullOrWhiteSpace(host.Notes))
        {
            var noteLines = host.Notes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in noteLines)
            {
                sb.AppendLine($"    # {line.Trim()}");
            }
        }
    }

    /// <summary>
    /// Generates port forwarding configuration lines.
    /// </summary>
    /// <param name="sb">The StringBuilder to append to.</param>
    /// <param name="profile">The port forwarding profile.</param>
    private static void GeneratePortForwardingConfig(StringBuilder sb, PortForwardingProfile profile)
    {
        switch (profile.ForwardingType)
        {
            case PortForwardingType.LocalForward:
                if (!string.IsNullOrWhiteSpace(profile.RemoteHost) && profile.RemotePort.HasValue)
                {
                    sb.AppendLine($"    LocalForward {profile.LocalPort} {profile.RemoteHost}:{profile.RemotePort.Value}");
                }
                break;

            case PortForwardingType.RemoteForward:
                if (!string.IsNullOrWhiteSpace(profile.RemoteHost) && profile.RemotePort.HasValue)
                {
                    string bindAddress = string.IsNullOrWhiteSpace(profile.LocalBindAddress)
                        ? "localhost"
                        : profile.LocalBindAddress;
                    sb.AppendLine($"    RemoteForward {profile.RemotePort.Value} {bindAddress}:{profile.LocalPort}");
                }
                break;

            case PortForwardingType.DynamicForward:
                sb.AppendLine($"    DynamicForward {profile.LocalPort}");
                break;
        }
    }

    /// <summary>
    /// Sanitizes a display name to create a valid SSH host alias.
    /// </summary>
    /// <param name="displayName">The display name to sanitize.</param>
    /// <returns>A sanitized host alias suitable for SSH config.</returns>
    private static string SanitizeHostAlias(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "unnamed-host";
        }

        // Convert to lowercase and replace spaces/underscores with hyphens
        var sanitized = displayName.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        // Remove all non-alphanumeric characters except hyphens
        sanitized = AlphanumericHyphenRegex().Replace(sanitized, "");

        // Remove leading/trailing hyphens
        sanitized = sanitized.Trim('-');

        // Replace multiple consecutive hyphens with single hyphen
        sanitized = MultipleHyphensRegex().Replace(sanitized, "-");

        // Ensure we have a valid result
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "unnamed-host";
        }

        return sanitized;
    }

    /// <summary>
    /// Exports the generated config to a file.
    /// </summary>
    /// <param name="filePath">The file path to write to.</param>
    /// <param name="hosts">The host entries to export.</param>
    /// <param name="options">Export options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExportToFileAsync(
        string filePath,
        IEnumerable<HostEntry> hosts,
        SshConfigExportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        var config = GenerateConfig(hosts, options);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, config, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Regex for removing non-alphanumeric characters except hyphens.
    /// </summary>
    [GeneratedRegex(@"[^a-z0-9\-]", RegexOptions.Compiled)]
    private static partial Regex AlphanumericHyphenRegex();

    /// <summary>
    /// Regex for replacing multiple consecutive hyphens with a single hyphen.
    /// </summary>
    [GeneratedRegex(@"-+", RegexOptions.Compiled)]
    private static partial Regex MultipleHyphensRegex();
}
