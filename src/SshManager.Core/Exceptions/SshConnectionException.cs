namespace SshManager.Core.Exceptions;

/// <summary>
/// Exception thrown when an SSH connection fails.
/// Provides structured information about the failure reason and connection details.
/// </summary>
public class SshConnectionException : SshManagerException
{
    /// <summary>
    /// Gets the categorized reason for the connection failure.
    /// </summary>
    public ConnectionFailedReason Reason { get; }

    /// <summary>
    /// Gets the hostname that the connection was attempted to.
    /// </summary>
    public string? Hostname { get; }

    /// <summary>
    /// Gets the port that the connection was attempted on.
    /// </summary>
    public int? Port { get; }

    /// <summary>
    /// Creates a new SshConnectionException.
    /// </summary>
    /// <param name="reason">The categorized reason for failure.</param>
    /// <param name="hostname">The target hostname.</param>
    /// <param name="port">The target port.</param>
    /// <param name="message">Technical error message.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public SshConnectionException(
        ConnectionFailedReason reason,
        string? hostname = null,
        int? port = null,
        string? message = null,
        Exception? innerException = null)
        : base(
            message ?? GetDefaultMessage(reason, hostname, port),
            GetUserFriendlyMessage(reason, hostname, port),
            GetErrorCode(reason),
            innerException)
    {
        Reason = reason;
        Hostname = hostname;
        Port = port;
    }

    private static string GetDefaultMessage(ConnectionFailedReason reason, string? hostname, int? port)
    {
        var target = hostname != null ? $" to {hostname}" : "";
        if (port.HasValue) target += $":{port}";

        return reason switch
        {
            ConnectionFailedReason.NetworkUnreachable => $"Network is unreachable{target}",
            ConnectionFailedReason.ConnectionRefused => $"Connection refused{target}",
            ConnectionFailedReason.ConnectionTimedOut => $"Connection timed out{target}",
            ConnectionFailedReason.DnsResolutionFailed => $"DNS resolution failed for {hostname ?? "host"}",
            ConnectionFailedReason.AuthenticationFailed => $"Authentication failed{target}",
            ConnectionFailedReason.HostKeyVerificationFailed => $"Host key verification failed{target}",
            ConnectionFailedReason.ProtocolError => $"SSH protocol error{target}",
            ConnectionFailedReason.ServerDisconnected => $"Server disconnected{target}",
            ConnectionFailedReason.Cancelled => "Connection cancelled by user",
            ConnectionFailedReason.ProxyConnectionFailed => "Proxy connection failed",
            _ => $"Connection failed{target}"
        };
    }

    private static string GetUserFriendlyMessage(ConnectionFailedReason reason, string? hostname, int? port)
    {
        var target = hostname ?? "the server";

        return reason switch
        {
            ConnectionFailedReason.NetworkUnreachable =>
                $"Cannot reach {target}. Check your network connection and ensure the server is accessible.",
            ConnectionFailedReason.ConnectionRefused =>
                $"Connection to {target} was refused. The SSH service may not be running or may be blocked by a firewall.",
            ConnectionFailedReason.ConnectionTimedOut =>
                $"Connection to {target} timed out. The server may be offline or unreachable.",
            ConnectionFailedReason.DnsResolutionFailed =>
                $"Could not resolve hostname '{hostname}'. Check the hostname spelling.",
            ConnectionFailedReason.AuthenticationFailed =>
                "Authentication failed. Check your username, password, or SSH key.",
            ConnectionFailedReason.HostKeyVerificationFailed =>
                $"Host key verification failed for {target}. The server's identity could not be verified.",
            ConnectionFailedReason.ProtocolError =>
                $"SSH protocol error connecting to {target}. The server may not support the required SSH version.",
            ConnectionFailedReason.ServerDisconnected =>
                $"The server {target} closed the connection unexpectedly.",
            ConnectionFailedReason.Cancelled =>
                "Connection was cancelled.",
            ConnectionFailedReason.ProxyConnectionFailed =>
                "Failed to connect through the proxy/jump host. Check the proxy server configuration.",
            _ => $"Failed to connect to {target}. Check the connection settings and try again."
        };
    }

    private static string GetErrorCode(ConnectionFailedReason reason)
    {
        return $"SSH_{reason.ToString().ToUpperInvariant()}";
    }
}
