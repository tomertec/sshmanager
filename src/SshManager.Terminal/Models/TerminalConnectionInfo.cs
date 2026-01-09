using SshManager.Core.Models;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Models;

/// <summary>
/// Connection parameters for establishing an SSH session.
/// </summary>
public sealed class TerminalConnectionInfo
{
    /// <summary>
    /// Hostname or IP address to connect to.
    /// </summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// SSH port number.
    /// </summary>
    public int Port { get; init; } = 22;

    /// <summary>
    /// SSH username.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Authentication method.
    /// </summary>
    public AuthType AuthType { get; init; }

    /// <summary>
    /// Decrypted password (for Password auth type).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Path to private key file (for PrivateKeyFile auth type).
    /// </summary>
    public string? PrivateKeyPath { get; init; }

    /// <summary>
    /// Passphrase for encrypted private key.
    /// </summary>
    public string? PrivateKeyPassphrase { get; init; }

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keep-alive interval (null or zero to disable).
    /// </summary>
    public TimeSpan? KeepAliveInterval { get; init; }

    /// <summary>
    /// The host entry ID for fingerprint storage (optional).
    /// </summary>
    public Guid? HostId { get; init; }

    /// <summary>
    /// When true, explicitly skips host key verification.
    /// Default is false, meaning host key verification is expected.
    /// </summary>
    /// <remarks>
    /// <para><strong>SECURITY WARNING:</strong> Setting this to <c>true</c> disables host key verification,
    /// which protects against man-in-the-middle (MITM) attacks. Only set this to <c>true</c> if you
    /// understand the security implications and have other means of verifying the server's identity.</para>
    ///
    /// <para>When this is <c>false</c> (the default) and no host key verification callback is provided
    /// to the connection service, a warning will be logged to alert about the potential security risk.</para>
    /// </remarks>
    public bool SkipHostKeyVerification { get; init; } = false;

    /// <summary>
    /// Environment variables to be set after the SSH connection is established.
    /// Each entry is a key-value pair (Name, Value) that will be exported in the shell.
    /// Only enabled environment variables should be included.
    /// </summary>
    public IReadOnlyList<EnvironmentVariableEntry> EnvironmentVariables { get; init; }
        = Array.Empty<EnvironmentVariableEntry>();

    /// <summary>
    /// The type of shell on the remote host for environment variable handling.
    /// </summary>
    /// <remarks>
    /// This determines how (or whether) environment variables are applied.
    /// POSIX shells use export syntax; non-POSIX shells skip environment variable application.
    /// </remarks>
    public ShellType ShellType { get; init; } = ShellType.Auto;

    /// <summary>
    /// Retry options for handling transient connection failures.
    /// Set to null to use default retry policy, or use <see cref="ConnectionRetryOptions.NoRetry"/>
    /// to disable retry behavior entirely.
    /// </summary>
    public ConnectionRetryOptions? RetryOptions { get; init; }

    /// <summary>
    /// Creates a connection info from a HostEntry and optional decrypted password.
    /// </summary>
    public static TerminalConnectionInfo FromHostEntry(
        HostEntry host,
        string? decryptedPassword = null,
        TimeSpan? timeout = null,
        TimeSpan? keepAliveInterval = null)
    {
        // Extract enabled environment variables, sorted by SortOrder
        // Use validated factory method to ensure proper sanitization
        var envVars = new List<EnvironmentVariableEntry>();
        foreach (var e in host.EnvironmentVariables.Where(e => e.IsEnabled).OrderBy(e => e.SortOrder))
        {
            if (EnvironmentVariableEntry.TryCreate(e.Name, e.Value, out var entry, out _))
            {
                envVars.Add(entry);
            }
            // Invalid entries are silently skipped - they should have been validated at save time
        }

        return new TerminalConnectionInfo
        {
            HostId = host.Id,
            Hostname = host.Hostname,
            Port = host.Port,
            Username = host.Username,
            AuthType = host.AuthType,
            Password = decryptedPassword,
            PrivateKeyPath = host.PrivateKeyPath,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
            KeepAliveInterval = keepAliveInterval,
            EnvironmentVariables = envVars,
            ShellType = host.ShellType
        };
    }
}

/// <summary>
/// Represents an environment variable to be set in the SSH session.
/// </summary>
/// <param name="Name">The environment variable name (must follow POSIX naming rules).</param>
/// <param name="Value">The environment variable value.</param>
/// <remarks>
/// <para>
/// Environment variable names must follow POSIX naming conventions:
/// - Start with a letter (a-z, A-Z) or underscore (_)
/// - Contain only letters, digits (0-9), or underscores
/// </para>
/// <para>
/// Use <see cref="TryCreate"/> for validated construction that rejects invalid names
/// and sanitizes values by removing NULL bytes and other dangerous control characters.
/// </para>
/// </remarks>
public readonly record struct EnvironmentVariableEntry(string Name, string Value)
{
    /// <summary>
    /// Maximum allowed length for environment variable names.
    /// </summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Maximum allowed length for environment variable values.
    /// </summary>
    public const int MaxValueLength = 4096;

    /// <summary>
    /// Creates a validated environment variable entry.
    /// </summary>
    /// <param name="name">The environment variable name.</param>
    /// <param name="value">The environment variable value.</param>
    /// <param name="entry">The created entry if validation succeeds.</param>
    /// <param name="error">The validation error message if validation fails.</param>
    /// <returns>True if the entry was created successfully, false if validation failed.</returns>
    /// <remarks>
    /// This method performs the following validations and sanitization:
    /// <list type="bullet">
    /// <item>Name must follow POSIX naming rules (letter/underscore start, alphanumeric/underscore body)</item>
    /// <item>Name cannot exceed <see cref="MaxNameLength"/> characters</item>
    /// <item>Value cannot exceed <see cref="MaxValueLength"/> characters</item>
    /// <item>NULL bytes (0x00) are removed from values as they can truncate strings</item>
    /// </list>
    /// </remarks>
    public static bool TryCreate(string name, string value, out EnvironmentVariableEntry entry, out string? error)
    {
        entry = default;
        error = null;

        // Validate name is not null or empty
        if (string.IsNullOrEmpty(name))
        {
            error = "Environment variable name cannot be empty.";
            return false;
        }

        // Validate name length
        if (name.Length > MaxNameLength)
        {
            error = $"Environment variable name cannot exceed {MaxNameLength} characters.";
            return false;
        }

        // Validate name follows POSIX naming rules
        if (!IsValidPosixName(name))
        {
            error = "Environment variable name must start with a letter or underscore and contain only letters, digits, or underscores.";
            return false;
        }

        // Validate value length
        var sanitizedValue = value ?? string.Empty;
        if (sanitizedValue.Length > MaxValueLength)
        {
            error = $"Environment variable value cannot exceed {MaxValueLength} characters.";
            return false;
        }

        // Sanitize value: remove NULL bytes which could truncate the string
        sanitizedValue = SanitizeValue(sanitizedValue);

        entry = new EnvironmentVariableEntry(name, sanitizedValue);
        return true;
    }

    /// <summary>
    /// Creates a validated environment variable entry, throwing on validation failure.
    /// </summary>
    /// <param name="name">The environment variable name.</param>
    /// <param name="value">The environment variable value.</param>
    /// <returns>The created entry.</returns>
    /// <exception cref="ArgumentException">Thrown if validation fails.</exception>
    public static EnvironmentVariableEntry Create(string name, string value)
    {
        if (!TryCreate(name, value, out var entry, out var error))
        {
            throw new ArgumentException(error);
        }
        return entry;
    }

    /// <summary>
    /// Validates that a string is a valid POSIX environment variable name.
    /// </summary>
    private static bool IsValidPosixName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // First character must be letter or underscore
        var first = name[0];
        if (!char.IsLetter(first) && first != '_')
        {
            return false;
        }

        // Remaining characters must be letters, digits, or underscores
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Sanitizes a value by removing potentially dangerous characters.
    /// </summary>
    private static string SanitizeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Remove NULL bytes which could truncate strings in C-based systems
        // Other control characters are handled by EscapeShellValue in SshConnectionService
        if (value.Contains('\0'))
        {
            return value.Replace("\0", string.Empty);
        }

        return value;
    }
}
