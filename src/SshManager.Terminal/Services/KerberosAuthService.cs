using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Provides Kerberos/GSSAPI authentication status using Windows SSPI.
/// </summary>
public sealed class KerberosAuthService : IKerberosAuthService
{
    private readonly ILogger<KerberosAuthService> _logger;
    private KerberosStatus? _cachedStatus;
    private DateTime _cacheTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public KerberosAuthService(ILogger<KerberosAuthService>? logger = null)
    {
        _logger = logger ?? NullLogger<KerberosAuthService>.Instance;
    }

    /// <inheritdoc />
    public async Task<KerberosStatus> GetStatusAsync(CancellationToken ct = default)
    {
        // Return cached status if still valid
        if (_cachedStatus != null && DateTime.UtcNow - _cacheTime < CacheDuration)
        {
            return _cachedStatus;
        }

        await RefreshAsync(ct);
        return _cachedStatus!;
    }

    /// <inheritdoc />
    public async Task<bool> HasValidTicketAsync(string? servicePrincipal = null, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct);
        return status.HasValidTgt;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            _cachedStatus = await DetectKerberosStatusAsync(ct);
            _cacheTime = DateTime.UtcNow;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<KerberosStatus> DetectKerberosStatusAsync(CancellationToken ct)
    {
        try
        {
            // Check if we're on Windows (Kerberos via SSPI is Windows-specific)
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogDebug("Kerberos status check skipped: not running on Windows");
                return KerberosStatus.NotAvailable("Kerberos authentication requires Windows");
            }

            // Get the current Windows identity
            using var identity = WindowsIdentity.GetCurrent();

            if (identity == null)
            {
                _logger.LogDebug("Could not get current Windows identity");
                return KerberosStatus.NotAvailable("Could not get current Windows identity");
            }

            // Check if the user is authenticated
            if (!identity.IsAuthenticated)
            {
                _logger.LogDebug("Current Windows identity is not authenticated");
                return KerberosStatus.NotAvailable("Windows user is not authenticated");
            }

            // Parse the principal name (username@REALM format or DOMAIN\username)
            var principalName = identity.Name;
            string? realm = null;
            string? principal = null;

            if (principalName.Contains('@'))
            {
                // UPN format: user@domain.com
                var parts = principalName.Split('@');
                principal = principalName;
                realm = parts.Length > 1 ? parts[1].ToUpperInvariant() : null;
            }
            else if (principalName.Contains('\\'))
            {
                // SAM format: DOMAIN\user
                var parts = principalName.Split('\\');
                realm = parts[0].ToUpperInvariant();
                principal = parts.Length > 1 ? $"{parts[1]}@{realm}" : principalName;
            }
            else
            {
                principal = principalName;
            }

            // Try to get TGT info using klist command
            var tgtInfo = await GetTgtInfoAsync(ct);

            if (tgtInfo.HasValidTgt)
            {
                _logger.LogDebug("Valid Kerberos TGT found for {Principal}, expires {Expiration}",
                    principal, tgtInfo.Expiration);
                return KerberosStatus.Valid(
                    realm ?? "UNKNOWN",
                    principal,
                    tgtInfo.Expiration ?? DateTimeOffset.MaxValue);
            }
            else
            {
                // Even without klist, if we're domain-joined we might still be able to authenticate
                // Check if this is a domain account
                var isDomainAccount = !string.IsNullOrEmpty(realm) &&
                    identity.AuthenticationType?.Contains("Kerberos", StringComparison.OrdinalIgnoreCase) == true;

                if (isDomainAccount)
                {
                    _logger.LogDebug("Domain account detected: {Principal}", principal);
                    return new KerberosStatus
                    {
                        IsAvailable = true,
                        HasValidTgt = true, // Assume TGT is available for domain accounts
                        Realm = realm,
                        Principal = principal,
                        StatusMessage = "Domain account (TGT available)"
                    };
                }

                _logger.LogDebug("No valid Kerberos TGT found for {Principal}", principal);
                return KerberosStatus.NoTicket(realm ?? "LOCAL", principal);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting Kerberos status");
            return KerberosStatus.NotAvailable($"Error: {ex.Message}");
        }
    }

    private async Task<(bool HasValidTgt, DateTimeOffset? Expiration)> GetTgtInfoAsync(CancellationToken ct)
    {
        try
        {
            // Use klist.exe to get Kerberos ticket information
            var psi = new ProcessStartInfo
            {
                FileName = "klist.exe",
                Arguments = "tgt",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                _logger.LogDebug("klist returned exit code {ExitCode}", process.ExitCode);
                return (false, null);
            }

            // Parse the klist output to find TGT expiration
            // Example output contains: "End Time: 1/18/2026 5:00:00 PM"
            var hasValidTgt = output.Contains("krbtgt", StringComparison.OrdinalIgnoreCase);
            DateTimeOffset? expiration = null;

            if (hasValidTgt)
            {
                // Try to parse expiration time from klist output
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("End Time:", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIndex = line.IndexOf(':');
                        if (colonIndex >= 0 && colonIndex < line.Length - 1)
                        {
                            var timeStr = line[(colonIndex + 1)..].Trim();
                            // Handle the format "1/18/2026 5:00:00 PM" where there's another colon in the time
                            var endTimeStart = line.IndexOf("End Time:", StringComparison.OrdinalIgnoreCase);
                            if (endTimeStart >= 0)
                            {
                                var afterLabel = line[(endTimeStart + "End Time:".Length)..].Trim();
                                if (DateTimeOffset.TryParse(afterLabel, out var parsed))
                                {
                                    expiration = parsed;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            _logger.LogDebug("klist check: HasValidTgt={HasValidTgt}, Expiration={Expiration}",
                hasValidTgt, expiration);

            return (hasValidTgt, expiration);
        }
        catch (Exception ex)
        {
            // klist.exe might not be available on all systems
            _logger.LogDebug(ex, "Could not run klist.exe to check TGT status");
            return (false, null);
        }
    }
}
