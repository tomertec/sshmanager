using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for importing SSH sessions from PuTTY's Windows Registry storage.
/// </summary>
public interface IPuttySessionImporter
{
    /// <summary>
    /// Checks if PuTTY is installed (has registry entries).
    /// </summary>
    /// <returns>True if PuTTY registry keys exist.</returns>
    bool IsPuttyInstalled();

    /// <summary>
    /// Gets all SSH sessions from PuTTY's registry.
    /// Non-SSH sessions (telnet, raw, etc.) are skipped with warnings.
    /// </summary>
    /// <returns>Result containing parsed sessions, warnings, and errors.</returns>
    PuttyImportResult GetAllSessions();

    /// <summary>
    /// Converts a PuTTY session to a HostEntry for storage.
    /// </summary>
    /// <param name="session">The PuTTY session to convert.</param>
    /// <returns>A new HostEntry populated from the PuTTY session.</returns>
    HostEntry ConvertToHostEntry(PuttySession session);
}
