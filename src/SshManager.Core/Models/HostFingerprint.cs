namespace SshManager.Core.Models;

/// <summary>
/// Stores SSH host key fingerprints for verification.
/// </summary>
public sealed class HostFingerprint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The host ID this fingerprint belongs to.
    /// </summary>
    public Guid HostId { get; set; }

    /// <summary>
    /// The host entry this fingerprint belongs to.
    /// </summary>
    public HostEntry? Host { get; set; }

    /// <summary>
    /// The key algorithm (e.g., "ssh-rsa", "ssh-ed25519", "ecdsa-sha2-nistp256").
    /// </summary>
    public string Algorithm { get; set; } = "";

    /// <summary>
    /// The SHA256 fingerprint in base64 format.
    /// </summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>
    /// When the fingerprint was first seen.
    /// </summary>
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the fingerprint was last verified.
    /// </summary>
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether this fingerprint has been trusted by the user.
    /// </summary>
    public bool IsTrusted { get; set; } = false;
}
