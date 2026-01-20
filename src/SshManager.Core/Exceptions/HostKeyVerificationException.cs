namespace SshManager.Core.Exceptions;

/// <summary>
/// Exception thrown when SSH host key verification fails.
/// Indicates a potential security issue - the server's identity could not be verified.
/// </summary>
public class HostKeyVerificationException : SshManagerException
{
    /// <summary>
    /// Gets the hostname of the server.
    /// </summary>
    public string? Hostname { get; }

    /// <summary>
    /// Gets the key type (e.g., "ssh-rsa", "ssh-ed25519").
    /// </summary>
    public string? KeyType { get; }

    /// <summary>
    /// Gets the fingerprint of the received key.
    /// </summary>
    public string? ReceivedFingerprint { get; }

    /// <summary>
    /// Gets the fingerprint that was expected (if known).
    /// </summary>
    public string? ExpectedFingerprint { get; }

    /// <summary>
    /// Gets whether this is a key mismatch (possible MITM attack).
    /// </summary>
    public bool IsKeyMismatch { get; }

    /// <summary>
    /// Creates a new HostKeyVerificationException.
    /// </summary>
    /// <param name="hostname">The server hostname.</param>
    /// <param name="keyType">The type of the host key.</param>
    /// <param name="receivedFingerprint">The fingerprint of the received key.</param>
    /// <param name="expectedFingerprint">The expected fingerprint (if known).</param>
    /// <param name="isKeyMismatch">Whether the key changed from a known value.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public HostKeyVerificationException(
        string? hostname = null,
        string? keyType = null,
        string? receivedFingerprint = null,
        string? expectedFingerprint = null,
        bool isKeyMismatch = false,
        Exception? innerException = null)
        : base(
            GetTechnicalMessage(hostname, isKeyMismatch),
            GetUserFriendlyMessage(hostname, isKeyMismatch),
            isKeyMismatch ? "HOST_KEY_CHANGED" : "HOST_KEY_UNKNOWN",
            innerException)
    {
        Hostname = hostname;
        KeyType = keyType;
        ReceivedFingerprint = receivedFingerprint;
        ExpectedFingerprint = expectedFingerprint;
        IsKeyMismatch = isKeyMismatch;
    }

    private static string GetTechnicalMessage(string? hostname, bool isKeyMismatch)
    {
        var host = hostname ?? "server";
        return isKeyMismatch
            ? $"Host key for {host} has changed"
            : $"Host key for {host} is unknown";
    }

    private static string GetUserFriendlyMessage(string? hostname, bool isKeyMismatch)
    {
        var host = hostname ?? "the server";

        if (isKeyMismatch)
        {
            return $"Warning: The host key for {host} has changed. " +
                   "This could indicate a man-in-the-middle attack, or the server was reinstalled. " +
                   "Verify the new key fingerprint before accepting.";
        }

        return $"The authenticity of {host} cannot be established. " +
               "This is normal when connecting to a new server for the first time.";
    }

    /// <summary>
    /// Creates a HostKeyVerificationException for an unknown host.
    /// </summary>
    public static HostKeyVerificationException UnknownHost(
        string hostname,
        string? keyType = null,
        string? fingerprint = null)
    {
        return new HostKeyVerificationException(
            hostname: hostname,
            keyType: keyType,
            receivedFingerprint: fingerprint,
            isKeyMismatch: false);
    }

    /// <summary>
    /// Creates a HostKeyVerificationException for a key mismatch.
    /// </summary>
    public static HostKeyVerificationException KeyMismatch(
        string hostname,
        string? keyType = null,
        string? receivedFingerprint = null,
        string? expectedFingerprint = null)
    {
        return new HostKeyVerificationException(
            hostname: hostname,
            keyType: keyType,
            receivedFingerprint: receivedFingerprint,
            expectedFingerprint: expectedFingerprint,
            isKeyMismatch: true);
    }
}
