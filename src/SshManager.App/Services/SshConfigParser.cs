using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.App.Services;

/// <summary>
/// Parses OpenSSH config files.
/// </summary>
public partial class SshConfigParser : ISshConfigParser
{
    private readonly ILogger<SshConfigParser> _logger;

    public SshConfigParser(ILogger<SshConfigParser>? logger = null)
    {
        _logger = logger ?? NullLogger<SshConfigParser>.Instance;
    }

    public string GetDefaultConfigPath()
    {
        var sshDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh");
        return Path.Combine(sshDir, "config");
    }

    public async Task<SshConfigParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var result = new SshConfigParseResult();

        if (!File.Exists(filePath))
        {
            result.Errors.Add($"File not found: {filePath}");
            return result;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);
            ParseLines(lines, result, Path.GetDirectoryName(filePath) ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse SSH config file: {FilePath}", filePath);
            result.Errors.Add($"Failed to read file: {ex.Message}");
        }

        return result;
    }

    private void ParseLines(string[] lines, SshConfigParseResult result, string configDir)
    {
        SshConfigHost? currentHost = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNum = i + 1;

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            // Parse key-value
            var match = KeyValueRegex().Match(line);
            if (!match.Success)
            {
                result.Warnings.Add($"Line {lineNum}: Could not parse: {line}");
                continue;
            }

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim('"', '\'');

            switch (key.ToLowerInvariant())
            {
                case "host":
                    // Save previous host if valid
                    if (currentHost != null && IsValidHost(currentHost))
                    {
                        result.Hosts.Add(currentHost);
                    }

                    // Skip wildcard hosts
                    if (value.Contains('*') || value.Contains('?'))
                    {
                        _logger.LogDebug("Skipping wildcard host pattern: {Pattern}", value);
                        currentHost = null;
                        continue;
                    }

                    currentHost = new SshConfigHost { Alias = value };
                    _logger.LogDebug("Found host: {Alias}", value);
                    break;

                case "hostname":
                    if (currentHost != null)
                        currentHost.Hostname = value;
                    break;

                case "port":
                    if (currentHost != null && int.TryParse(value, out var port))
                        currentHost.Port = port;
                    break;

                case "user":
                    if (currentHost != null)
                        currentHost.User = value;
                    break;

                case "identityfile":
                    if (currentHost != null)
                        currentHost.IdentityFile = ExpandPath(value);
                    break;

                case "proxyjump":
                    if (currentHost != null)
                        currentHost.ProxyJump = value;
                    break;

                case "localforward":
                    if (currentHost != null)
                    {
                        var localForward = ParseLocalForward(value, lineNum, result);
                        if (localForward != null)
                            currentHost.LocalForwards.Add(localForward);
                    }
                    break;

                case "remoteforward":
                    if (currentHost != null)
                    {
                        var remoteForward = ParseRemoteForward(value, lineNum, result);
                        if (remoteForward != null)
                            currentHost.RemoteForwards.Add(remoteForward);
                    }
                    break;

                case "dynamicforward":
                    if (currentHost != null)
                    {
                        var dynamicForward = ParseDynamicForward(value, lineNum, result);
                        if (dynamicForward != null)
                            currentHost.DynamicForwards.Add(dynamicForward);
                    }
                    break;

                case "include":
                    // Handle Include directive
                    var includePath = ExpandPath(value);
                    if (!Path.IsPathRooted(includePath))
                    {
                        includePath = Path.Combine(configDir, includePath);
                    }
                    result.Warnings.Add($"Line {lineNum}: Include directive found ({value}) - not followed");
                    break;

                case "match":
                    // Match blocks are complex, skip the current host context
                    if (currentHost != null && IsValidHost(currentHost))
                    {
                        result.Hosts.Add(currentHost);
                    }
                    currentHost = null;
                    result.Warnings.Add($"Line {lineNum}: Match block skipped");
                    break;

                // Known directives we don't handle but shouldn't warn about
                case "addkeystoagent":
                case "addressfamily":
                case "batchmode":
                case "canonicaldomains":
                case "canonicalizefallbacklocal":
                case "canonicalizehostname":
                case "canonicalizemaxdots":
                case "canonicalizepermittedcnames":
                case "certificatefile":
                case "checkhostip":
                case "cipher":
                case "ciphers":
                case "clearallforwardings":
                case "compression":
                case "connectionattempts":
                case "connecttimeout":
                case "controlmaster":
                case "controlpath":
                case "controlpersist":
                case "enablesshkeysign":
                case "escapechar":
                case "exitonforwardfailure":
                case "fingerprinthash":
                case "forwardagent":
                case "forwardx11":
                case "forwardx11timeout":
                case "forwardx11trusted":
                case "gatewayports":
                case "globalknownhostsfile":
                case "gssapiauthentication":
                case "gssapidelegatecredentials":
                case "gssapikeyexchange":
                case "gssapirenewalforcerekey":
                case "gssapiserveridentity":
                case "gssapitrustdns":
                case "hashknownhosts":
                case "hostbasedauthentication":
                case "hostbasedkeytypes":
                case "hostkeyalgorithms":
                case "hostkeyalias":
                case "identitiesonly":
                case "identityagent":
                case "ipqos":
                case "kbdinteractiveauthentication":
                case "kbdinteractivedevices":
                case "kexalgorithms":
                case "localcommand":
                case "loglevel":
                case "macs":
                case "nohostauthenticationforlocalhost":
                case "numberofpasswordprompts":
                case "passwordauthentication":
                case "permitlocalcommand":
                case "pkcs11provider":
                case "preferredauthentications":
                case "proxycommand":
                case "proxyusefdpass":
                case "pubkeyacceptedalgorithms":
                case "pubkeyacceptedkeytypes":
                case "pubkeyauthentication":
                case "rekeylimit":
                case "remotecommand":
                case "requesttty":
                case "revokedhostkeys":
                case "sendenv":
                case "serveralivecountmax":
                case "serveraliveinterval":
                case "setenv":
                case "streamlocalbindmask":
                case "streamlocalbindunlink":
                case "stricthostkeychecking":
                case "tcpkeepalive":
                case "tunnel":
                case "tunneldevice":
                case "updatehostkeys":
                case "userknownhostsfile":
                case "verifyhostkeydns":
                case "visualhostkey":
                case "xauthlocation":
                    // Known directives - silently ignore
                    break;

                default:
                    _logger.LogDebug("Unknown directive: {Key}", key);
                    break;
            }
        }

        // Don't forget the last host
        if (currentHost != null && IsValidHost(currentHost))
        {
            result.Hosts.Add(currentHost);
        }

        _logger.LogInformation("Parsed {HostCount} hosts from SSH config", result.Hosts.Count);
    }

    private static bool IsValidHost(SshConfigHost host)
    {
        // Must have an alias at minimum
        return !string.IsNullOrWhiteSpace(host.Alias);
    }

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Expand ~ to user's home directory
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path[2..]);
        }
        else if (path == "~")
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        // Normalize path separators for Windows
        path = path.Replace('/', Path.DirectorySeparatorChar);

        return path;
    }

    [GeneratedRegex(@"^\s*(\S+)\s+(.+)$")]
    private static partial Regex KeyValueRegex();

    /// <summary>
    /// Parses a LocalForward directive value.
    /// Format: [bind_address:]port host:hostport
    /// Examples: "8080 localhost:80", "127.0.0.1:8080 webserver:80"
    /// </summary>
    private LocalForwardEntry? ParseLocalForward(string value, int lineNum, SshConfigParseResult result)
    {
        try
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid LocalForward format: {value}");
                return null;
            }

            // Parse local side: [bind_address:]port
            var (localBindAddress, localPort) = ParseBindAddressPort(parts[0], "127.0.0.1");
            if (localPort == null)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid local port in LocalForward: {parts[0]}");
                return null;
            }

            // Parse remote side: host:port
            var remoteMatch = HostPortRegex().Match(parts[1]);
            if (!remoteMatch.Success)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid remote host:port in LocalForward: {parts[1]}");
                return null;
            }

            var remoteHost = remoteMatch.Groups[1].Value;
            if (!int.TryParse(remoteMatch.Groups[2].Value, out var remotePort) || remotePort < 1 || remotePort > 65535)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid remote port in LocalForward: {parts[1]}");
                return null;
            }

            _logger.LogDebug("Parsed LocalForward: {BindAddress}:{LocalPort} -> {RemoteHost}:{RemotePort}",
                localBindAddress, localPort, remoteHost, remotePort);

            return new LocalForwardEntry(localBindAddress, localPort.Value, remoteHost, remotePort);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Line {lineNum}: Failed to parse LocalForward: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a RemoteForward directive value.
    /// Format: [bind_address:]port host:hostport
    /// Examples: "8080 localhost:80", "0.0.0.0:8080 webserver:80"
    /// </summary>
    private RemoteForwardEntry? ParseRemoteForward(string value, int lineNum, SshConfigParseResult result)
    {
        try
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid RemoteForward format: {value}");
                return null;
            }

            // Parse remote side (on server): [bind_address:]port
            var (remoteBindAddress, remotePort) = ParseBindAddressPort(parts[0], "localhost");
            if (remotePort == null)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid remote port in RemoteForward: {parts[0]}");
                return null;
            }

            // Parse local side: host:port
            var localMatch = HostPortRegex().Match(parts[1]);
            if (!localMatch.Success)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid local host:port in RemoteForward: {parts[1]}");
                return null;
            }

            var localHost = localMatch.Groups[1].Value;
            if (!int.TryParse(localMatch.Groups[2].Value, out var localPort) || localPort < 1 || localPort > 65535)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid local port in RemoteForward: {parts[1]}");
                return null;
            }

            _logger.LogDebug("Parsed RemoteForward: {BindAddress}:{RemotePort} -> {LocalHost}:{LocalPort}",
                remoteBindAddress, remotePort, localHost, localPort);

            return new RemoteForwardEntry(remoteBindAddress, remotePort.Value, localHost, localPort);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Line {lineNum}: Failed to parse RemoteForward: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a DynamicForward directive value.
    /// Format: [bind_address:]port
    /// Examples: "1080", "127.0.0.1:1080"
    /// </summary>
    private DynamicForwardEntry? ParseDynamicForward(string value, int lineNum, SshConfigParseResult result)
    {
        try
        {
            var (bindAddress, port) = ParseBindAddressPort(value.Trim(), "127.0.0.1");
            if (port == null)
            {
                result.Warnings.Add($"Line {lineNum}: Invalid port in DynamicForward: {value}");
                return null;
            }

            _logger.LogDebug("Parsed DynamicForward: {BindAddress}:{Port}", bindAddress, port);

            return new DynamicForwardEntry(bindAddress, port.Value);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Line {lineNum}: Failed to parse DynamicForward: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses a bind address and port from formats like "port" or "address:port".
    /// </summary>
    /// <param name="value">The value to parse.</param>
    /// <param name="defaultAddress">Default address if only port is specified.</param>
    /// <returns>Tuple of (address, port) where port may be null if invalid.</returns>
    private static (string address, int? port) ParseBindAddressPort(string value, string defaultAddress)
    {
        // Check if it's just a port number
        if (int.TryParse(value, out var simplePort) && simplePort >= 1 && simplePort <= 65535)
        {
            return (defaultAddress, simplePort);
        }

        // Check for IPv6 format: [address]:port
        if (value.StartsWith('['))
        {
            var closeBracket = value.IndexOf(']');
            if (closeBracket > 0 && closeBracket < value.Length - 2 && value[closeBracket + 1] == ':')
            {
                var ipv6Address = value[1..closeBracket];
                var portStr = value[(closeBracket + 2)..];
                if (int.TryParse(portStr, out var port) && port >= 1 && port <= 65535)
                {
                    return (ipv6Address, port);
                }
            }
            return (defaultAddress, null);
        }

        // Standard format: address:port
        var lastColon = value.LastIndexOf(':');
        if (lastColon > 0 && lastColon < value.Length - 1)
        {
            var address = value[..lastColon];
            var portStr = value[(lastColon + 1)..];
            if (int.TryParse(portStr, out var port) && port >= 1 && port <= 65535)
            {
                return (address, port);
            }
        }

        return (defaultAddress, null);
    }

    /// <summary>
    /// Regex for matching host:port format.
    /// </summary>
    [GeneratedRegex(@"^(.+):(\d+)$")]
    private static partial Regex HostPortRegex();
}
