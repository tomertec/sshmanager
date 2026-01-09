namespace SshManager.App.Services;

/// <summary>
/// Represents a PuTTY session parsed from the Windows Registry.
/// </summary>
public class PuttySession
{
    /// <summary>
    /// The session name (decoded from URL encoding).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The hostname or IP address.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// SSH port number (default: 22).
    /// </summary>
    public int Port { get; set; } = 22;

    /// <summary>
    /// Protocol type (ssh, telnet, raw, rlogin, serial).
    /// </summary>
    public string Protocol { get; set; } = "ssh";

    /// <summary>
    /// Username for the connection.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Path to the private key file (.ppk format).
    /// </summary>
    public string? PrivateKeyFile { get; set; }
}

/// <summary>
/// Result of importing PuTTY sessions from the registry.
/// </summary>
public class PuttyImportResult
{
    /// <summary>
    /// Successfully parsed SSH sessions.
    /// </summary>
    public List<PuttySession> Sessions { get; } = new();

    /// <summary>
    /// Warning messages (e.g., skipped non-SSH sessions).
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Error messages.
    /// </summary>
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Whether PuTTY registry entries were found.
    /// </summary>
    public bool IsPuttyInstalled { get; set; }

    /// <summary>
    /// Whether any errors occurred during import.
    /// </summary>
    public bool HasErrors => Errors.Count > 0;
}
