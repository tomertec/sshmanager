namespace SshManager.Terminal.Services;

/// <summary>
/// Provides Kerberos/GSSAPI authentication status and diagnostics.
/// </summary>
public interface IKerberosAuthService
{
    /// <summary>
    /// Gets the current Kerberos authentication status.
    /// </summary>
    Task<KerberosStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a valid Kerberos ticket exists for the specified service principal.
    /// </summary>
    /// <param name="servicePrincipal">The service principal to check, or null to check for any valid TGT.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a valid ticket exists, false otherwise.</returns>
    Task<bool> HasValidTicketAsync(string? servicePrincipal = null, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the cached Kerberos status.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents the current Kerberos authentication status.
/// </summary>
public sealed record KerberosStatus
{
    /// <summary>
    /// Whether Kerberos is available on this system.
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Whether a valid Ticket Granting Ticket (TGT) exists.
    /// </summary>
    public bool HasValidTgt { get; init; }

    /// <summary>
    /// The Kerberos realm (domain) for the current user.
    /// Example: "CORP.EXAMPLE.COM"
    /// </summary>
    public string? Realm { get; init; }

    /// <summary>
    /// The Kerberos principal (username@REALM) for the current user.
    /// Example: "jsmith@CORP.EXAMPLE.COM"
    /// </summary>
    public string? Principal { get; init; }

    /// <summary>
    /// When the TGT expires, or null if no valid TGT.
    /// </summary>
    public DateTimeOffset? TgtExpiration { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string StatusMessage { get; init; } = "Unknown";

    /// <summary>
    /// Any error that occurred while checking Kerberos status.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a status indicating Kerberos is not available.
    /// </summary>
    public static KerberosStatus NotAvailable(string? error = null) => new()
    {
        IsAvailable = false,
        HasValidTgt = false,
        StatusMessage = error ?? "Kerberos is not available on this system",
        Error = error
    };

    /// <summary>
    /// Creates a status indicating no valid TGT exists.
    /// </summary>
    public static KerberosStatus NoTicket(string realm, string principal) => new()
    {
        IsAvailable = true,
        HasValidTgt = false,
        Realm = realm,
        Principal = principal,
        StatusMessage = "No valid Kerberos ticket"
    };

    /// <summary>
    /// Creates a status indicating a valid TGT exists.
    /// </summary>
    public static KerberosStatus Valid(string realm, string principal, DateTimeOffset expiration) => new()
    {
        IsAvailable = true,
        HasValidTgt = true,
        Realm = realm,
        Principal = principal,
        TgtExpiration = expiration,
        StatusMessage = $"Valid until {expiration:g}"
    };
}
