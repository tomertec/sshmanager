using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Security;

/// <summary>
/// Encrypts and decrypts sensitive data using Windows DPAPI (Data Protection API).
/// Data is protected for the current user only and cannot be decrypted by other users
/// or on different machines.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private readonly ILogger<DpapiSecretProtector> _logger;

    // Entropy adds an extra layer of protection - keeps it app-specific
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(SecurityConstants.Dpapi.EntropyString);

    public DpapiSecretProtector(ILogger<DpapiSecretProtector>? logger = null)
    {
        _logger = logger ?? NullLogger<DpapiSecretProtector>.Instance;
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        try
        {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] protectedBytes = ProtectedData.Protect(
                plaintextBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            _logger.LogDebug("Successfully protected secret data");
            return Convert.ToBase64String(protectedBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "DPAPI encryption failed");
            throw;
        }
    }

    /// <inheritdoc />
    public string Unprotect(string protectedBase64)
    {
        ArgumentNullException.ThrowIfNull(protectedBase64);

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(protectedBase64);
            byte[] plaintextBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            _logger.LogDebug("Successfully unprotected secret data");
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "DPAPI decryption failed - data may have been encrypted by a different user or on a different machine");
            throw;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "DPAPI decryption failed - invalid base64 format");
            throw;
        }
    }

    /// <inheritdoc />
    public string? TryUnprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
            return null;

        try
        {
            return Unprotect(protectedBase64);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt protected data - returning null");
            return null;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt protected data (invalid format) - returning null");
            return null;
        }
    }
}
