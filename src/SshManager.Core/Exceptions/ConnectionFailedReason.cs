namespace SshManager.Core.Exceptions;

/// <summary>
/// Enumeration of possible reasons for connection failure.
/// Provides structured categorization for error handling and user feedback.
/// </summary>
public enum ConnectionFailedReason
{
    /// <summary>
    /// The cause of the connection failure is unknown or not categorized.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The network is unreachable (no route to host).
    /// </summary>
    NetworkUnreachable,

    /// <summary>
    /// The connection was actively refused by the remote host.
    /// </summary>
    ConnectionRefused,

    /// <summary>
    /// The connection attempt timed out.
    /// </summary>
    ConnectionTimedOut,

    /// <summary>
    /// DNS resolution failed for the hostname.
    /// </summary>
    DnsResolutionFailed,

    /// <summary>
    /// Authentication failed (wrong password, key rejected, etc.).
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    /// Host key verification failed (key mismatch or unknown host).
    /// </summary>
    HostKeyVerificationFailed,

    /// <summary>
    /// SSH protocol error (version mismatch, algorithm negotiation failed, etc.).
    /// </summary>
    ProtocolError,

    /// <summary>
    /// The server disconnected unexpectedly.
    /// </summary>
    ServerDisconnected,

    /// <summary>
    /// The port is not available or access is denied.
    /// </summary>
    PortNotAvailable,

    /// <summary>
    /// The serial port device was not found.
    /// </summary>
    DeviceNotFound,

    /// <summary>
    /// Permission denied to access the resource.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// Invalid configuration or parameters.
    /// </summary>
    InvalidConfiguration,

    /// <summary>
    /// The connection was cancelled by the user.
    /// </summary>
    Cancelled,

    /// <summary>
    /// A proxy or jump host in the chain failed.
    /// </summary>
    ProxyConnectionFailed
}
