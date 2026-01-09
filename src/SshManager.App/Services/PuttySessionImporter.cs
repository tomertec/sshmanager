using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Imports SSH sessions from PuTTY's Windows Registry storage.
/// PuTTY stores sessions at: HKEY_CURRENT_USER\Software\SimonTatham\PuTTY\Sessions\
/// </summary>
public class PuttySessionImporter : IPuttySessionImporter
{
    private const string PuttySessionsPath = @"Software\SimonTatham\PuTTY\Sessions";
    private readonly ILogger<PuttySessionImporter> _logger;

    public PuttySessionImporter(ILogger<PuttySessionImporter>? logger = null)
    {
        _logger = logger ?? NullLogger<PuttySessionImporter>.Instance;
    }

    /// <inheritdoc />
    public bool IsPuttyInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PuttySessionsPath);
            return key != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if PuTTY is installed");
            return false;
        }
    }

    /// <inheritdoc />
    public PuttyImportResult GetAllSessions()
    {
        var result = new PuttyImportResult();

        try
        {
            using var sessionsKey = Registry.CurrentUser.OpenSubKey(PuttySessionsPath);
            if (sessionsKey == null)
            {
                result.IsPuttyInstalled = false;
                result.Errors.Add("PuTTY is not installed or has no saved sessions.");
                _logger.LogInformation("PuTTY registry key not found at {Path}", PuttySessionsPath);
                return result;
            }

            result.IsPuttyInstalled = true;
            var subKeyNames = sessionsKey.GetSubKeyNames();
            _logger.LogDebug("Found {Count} PuTTY session entries", subKeyNames.Length);

            foreach (var encodedName in subKeyNames)
            {
                // Skip "Default Settings" session
                if (encodedName.Equals("Default%20Settings", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping Default Settings session");
                    continue;
                }

                try
                {
                    using var sessionKey = sessionsKey.OpenSubKey(encodedName);
                    if (sessionKey == null)
                    {
                        _logger.LogWarning("Could not open session key: {EncodedName}", encodedName);
                        continue;
                    }

                    var decodedName = HttpUtility.UrlDecode(encodedName);
                    var protocol = GetStringValue(sessionKey, "Protocol") ?? "ssh";

                    // Only import SSH sessions
                    if (!protocol.Equals("ssh", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"Skipped non-SSH session: {decodedName} (protocol: {protocol})");
                        _logger.LogDebug("Skipping non-SSH session: {Name} ({Protocol})", decodedName, protocol);
                        continue;
                    }

                    var hostName = GetStringValue(sessionKey, "HostName");
                    if (string.IsNullOrWhiteSpace(hostName))
                    {
                        result.Warnings.Add($"Skipped session with no hostname: {decodedName}");
                        _logger.LogDebug("Skipping session with no hostname: {Name}", decodedName);
                        continue;
                    }

                    var session = new PuttySession
                    {
                        Name = decodedName,
                        HostName = hostName,
                        Port = GetIntValue(sessionKey, "PortNumber", 22),
                        Protocol = protocol,
                        UserName = GetStringValue(sessionKey, "UserName"),
                        PrivateKeyFile = GetStringValue(sessionKey, "PublicKeyFile")
                    };

                    result.Sessions.Add(session);
                    _logger.LogDebug("Found PuTTY session: {Name} -> {Host}:{Port}",
                        session.Name, session.HostName, session.Port);
                }
                catch (Exception ex)
                {
                    var decodedName = HttpUtility.UrlDecode(encodedName);
                    result.Errors.Add($"Error reading session '{decodedName}': {ex.Message}");
                    _logger.LogWarning(ex, "Error reading PuTTY session: {EncodedName}", encodedName);
                }
            }

            _logger.LogInformation("Found {Count} PuTTY SSH sessions", result.Sessions.Count);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error accessing PuTTY registry: {ex.Message}");
            _logger.LogError(ex, "Error accessing PuTTY registry");
        }

        return result;
    }

    /// <inheritdoc />
    public HostEntry ConvertToHostEntry(PuttySession session)
    {
        var host = new HostEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = session.Name,
            Hostname = session.HostName ?? "",
            Port = session.Port,
            Username = session.UserName ?? Environment.UserName,
            Notes = "Imported from PuTTY",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Determine auth type based on key file presence
        if (!string.IsNullOrEmpty(session.PrivateKeyFile))
        {
            host.AuthType = AuthType.PrivateKeyFile;
            host.PrivateKeyPath = session.PrivateKeyFile;

            // Warn about .ppk format - SSH.NET doesn't support it directly
            if (session.PrivateKeyFile.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase))
            {
                host.Notes += "\n\nNote: PuTTY key file (.ppk) may need conversion to OpenSSH format using PuTTYgen.";
            }
        }
        else
        {
            host.AuthType = AuthType.SshAgent;
        }

        return host;
    }

    private static string? GetStringValue(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        return value as string;
    }

    private static int GetIntValue(RegistryKey key, string name, int defaultValue)
    {
        var value = key.GetValue(name);
        return value is int intValue ? intValue : defaultValue;
    }
}
