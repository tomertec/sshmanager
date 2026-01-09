using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using SshNet.Agent;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for diagnosing SSH agent availability and inspecting loaded keys.
/// Supports both Pageant (PuTTY's SSH agent) and OpenSSH Agent on Windows.
/// Implements caching with manual refresh capability to reduce overhead of repeated agent queries.
/// </summary>
public sealed class AgentDiagnosticsService : IAgentDiagnosticsService
{
    private readonly ILogger<AgentDiagnosticsService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private AgentDiagnosticResult? _cachedResult;
    private DateTime _lastRefresh = DateTime.MinValue;

    public AgentDiagnosticsService(ILogger<AgentDiagnosticsService>? logger = null)
    {
        _logger = logger ?? NullLogger<AgentDiagnosticsService>.Instance;
    }

    /// <inheritdoc />
    public bool IsPageantAvailable => _cachedResult?.PageantAvailable ?? false;

    /// <inheritdoc />
    public bool IsOpenSshAgentAvailable => _cachedResult?.OpenSshAgentAvailable ?? false;

    /// <inheritdoc />
    public string? ActiveAgentType => _cachedResult?.ActiveAgentType;

    /// <inheritdoc />
    public int AvailableKeyCount => _cachedResult?.Keys.Count ?? 0;

    /// <inheritdoc />
    public async Task<AgentDiagnosticResult> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        // Return cached result if available
        if (_cachedResult != null)
        {
            _logger.LogDebug("Returning cached diagnostic result from {LastRefresh}", _lastRefresh);
            return _cachedResult;
        }

        // Perform initial scan
        await RefreshAsync(ct);
        return _cachedResult!;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            _logger.LogDebug("Refreshing SSH agent diagnostics");
            _cachedResult = await PerformDiagnosticScanAsync(ct);
            _lastRefresh = DateTime.UtcNow;
            _logger.LogInformation(
                "Diagnostic scan complete: Pageant={Pageant}, OpenSSH={OpenSSH}, Active={Active}, Keys={KeyCount}",
                _cachedResult.PageantAvailable,
                _cachedResult.OpenSshAgentAvailable,
                _cachedResult.ActiveAgentType ?? "None",
                _cachedResult.Keys.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Performs a diagnostic scan of available SSH agents and their loaded keys.
    /// Tries Pageant first, then OpenSSH Agent (matching SshAuthenticationFactory logic).
    /// </summary>
    private async Task<AgentDiagnosticResult> PerformDiagnosticScanAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            bool pageantAvailable = false;
            bool openSshAvailable = false;
            string? activeAgent = null;
            var keys = new List<AgentKeyInfo>();
            string? errorMessage = null;

            try
            {
                // Try Pageant first (most common on Windows with PuTTY)
                var pageantResult = TryGetPageantKeys();
                pageantAvailable = pageantResult.Available;

                if (pageantResult.Keys.Count > 0)
                {
                    activeAgent = "Pageant";
                    keys.AddRange(pageantResult.Keys);
                    _logger.LogDebug("Pageant is active with {Count} keys", pageantResult.Keys.Count);
                }
                else if (pageantAvailable)
                {
                    _logger.LogDebug("Pageant is running but has no keys loaded");
                }

                // Try OpenSSH Agent (Windows named pipe or Unix socket)
                var openSshResult = TryGetOpenSshAgentKeys();
                openSshAvailable = openSshResult.Available;

                if (openSshResult.Keys.Count > 0 && activeAgent == null)
                {
                    // Only use OpenSSH Agent if Pageant had no keys
                    activeAgent = "OpenSSH Agent";
                    keys.AddRange(openSshResult.Keys);
                    _logger.LogDebug("OpenSSH Agent is active with {Count} keys", openSshResult.Keys.Count);
                }
                else if (openSshResult.Keys.Count > 0)
                {
                    _logger.LogDebug("OpenSSH Agent has {Count} keys but Pageant is active", openSshResult.Keys.Count);
                }
                else if (openSshAvailable)
                {
                    _logger.LogDebug("OpenSSH Agent is running but has no keys loaded");
                }

                if (!pageantAvailable && !openSshAvailable)
                {
                    errorMessage = "No SSH agent detected. Install Pageant or start OpenSSH Agent service.";
                    _logger.LogDebug("No SSH agents available");
                }
                else if (keys.Count == 0)
                {
                    errorMessage = "SSH agent is running but has no keys loaded.";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error scanning SSH agents: {ex.Message}";
                _logger.LogWarning(ex, "Failed to scan SSH agents");
            }

            return new AgentDiagnosticResult(
                pageantAvailable,
                openSshAvailable,
                activeAgent,
                keys,
                errorMessage);

        }, ct);
    }

    /// <summary>
    /// Attempts to retrieve keys from Pageant.
    /// </summary>
    private AgentScanResult TryGetPageantKeys()
    {
        var keyInfos = new List<AgentKeyInfo>();
        bool available = false;

        try
        {
            var pageant = new Pageant();
            var keys = pageant.RequestIdentities().ToList();
            available = true; // Connection successful

            foreach (var key in keys)
            {
                try
                {
                    // AgentIdentity implements IPrivateKeySource
                    var keyInfo = ExtractKeyInfo((IPrivateKeySource)key);
                    if (keyInfo != null)
                    {
                        keyInfos.Add(keyInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract info from Pageant key");
                }
            }

            _logger.LogDebug("Pageant scan: {Count} identities, {KeyCount} keys extracted", keys.Count, keyInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pageant not available: {Message}", ex.Message);
        }

        return new AgentScanResult(available, keyInfos);
    }

    /// <summary>
    /// Attempts to retrieve keys from OpenSSH Agent.
    /// </summary>
    private AgentScanResult TryGetOpenSshAgentKeys()
    {
        var keyInfos = new List<AgentKeyInfo>();
        bool available = false;

        try
        {
            var agent = new SshAgent();
            var keys = agent.RequestIdentities().ToList();
            available = true; // Connection successful

            foreach (var key in keys)
            {
                try
                {
                    // AgentIdentity implements IPrivateKeySource
                    var keyInfo = ExtractKeyInfo((IPrivateKeySource)key);
                    if (keyInfo != null)
                    {
                        keyInfos.Add(keyInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract info from OpenSSH Agent key");
                }
            }

            _logger.LogDebug("OpenSSH Agent scan: {Count} identities, {KeyCount} keys extracted", keys.Count, keyInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OpenSSH Agent not available: {Message}", ex.Message);
        }

        return new AgentScanResult(available, keyInfos);
    }

    /// <summary>
    /// Extracts key information from an IPrivateKeySource (agent identity).
    /// </summary>
    private AgentKeyInfo? ExtractKeyInfo(IPrivateKeySource keySource)
    {
        try
        {
            // Get key type from HostKeyAlgorithms (first available algorithm)
            string keyType = "unknown";

            // IPrivateKeySource exposes HostKeyAlgorithms (HostAlgorithm objects with Name property)
            var algorithms = keySource.HostKeyAlgorithms.ToList();
            if (algorithms.Count > 0)
            {
                keyType = algorithms[0].Name ?? "unknown";
            }

            // For fingerprint and key size, we need the actual public key data
            // Since IPrivateKeySource doesn't directly expose the key bytes,
            // we'll use a workaround: create a temporary PrivateKeyAuthenticationMethod
            // and extract the key data from its HostKey property
            string fingerprint = "unknown";
            int keySizeBits = 0;

            try
            {
                // Create a temporary auth method to access the host key
                var tempAuth = new PrivateKeyAuthenticationMethod("temp", keySource);

                // The PrivateKeyAuthenticationMethod doesn't expose HostKey directly either
                // So we'll compute a simpler fingerprint from the key type name
                fingerprint = $"{keyType}"; // Simplified - just use key type for now
                keySizeBits = EstimateKeySizeFromType(keyType);
            }
            catch
            {
                // Fall back to type-based estimation
                keySizeBits = EstimateKeySizeFromType(keyType);
            }

            // Try to extract comment - unfortunately IPrivateKeySource doesn't expose this
            string? comment = null;

            return new AgentKeyInfo(fingerprint, keyType, comment, keySizeBits);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract key info from agent identity");
            return null;
        }
    }

    /// <summary>
    /// Extracts the key type string from a host key.
    /// SSH public keys start with a 4-byte length prefix, then the key type string.
    /// </summary>
    private string ExtractKeyType(byte[] hostKey)
    {
        try
        {
            if (hostKey.Length < 8)
                return "unknown";

            // Read the length of the key type string (big-endian 32-bit integer)
            int typeLength = ReadInt32BigEndian(hostKey, 0);

            if (typeLength <= 0 || typeLength > 100 || hostKey.Length < 4 + typeLength)
                return "unknown";

            // Extract the key type string
            return Encoding.ASCII.GetString(hostKey, 4, typeLength);
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Calculates the SHA-256 fingerprint of a public key.
    /// Returns the fingerprint in the format used by modern SSH (base64-encoded SHA-256).
    /// </summary>
    private string CalculateFingerprint(byte[] hostKey)
    {
        try
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(hostKey);
            return $"SHA256:{Convert.ToBase64String(hash).TrimEnd('=')}";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Estimates the key size in bits based only on the key type string.
    /// Used when we don't have access to the actual key data.
    /// </summary>
    private int EstimateKeySizeFromType(string keyType)
    {
        return keyType.ToLowerInvariant() switch
        {
            // Ed25519 keys are always 256 bits
            "ssh-ed25519" => 256,

            // ECDSA keys have standard sizes based on curve
            "ecdsa-sha2-nistp256" => 256,
            "ecdsa-sha2-nistp384" => 384,
            "ecdsa-sha2-nistp521" => 521,

            // RSA keys - use most common size as default
            "ssh-rsa" or "rsa-sha2-256" or "rsa-sha2-512" => 2048,

            // DSA keys - standard size
            "ssh-dss" => 1024,

            _ => 0 // Unknown key type
        };
    }

    /// <summary>
    /// Estimates the key size in bits based on the key type and host key data.
    /// </summary>
    private int EstimateKeySize(byte[] hostKey, string keyType)
    {
        try
        {
            return keyType.ToLowerInvariant() switch
            {
                // Ed25519 keys are always 256 bits
                "ssh-ed25519" => 256,

                // ECDSA keys have standard sizes based on curve
                "ecdsa-sha2-nistp256" => 256,
                "ecdsa-sha2-nistp384" => 384,
                "ecdsa-sha2-nistp521" => 521,

                // RSA and DSA keys - estimate from host key length
                "ssh-rsa" or "rsa-sha2-256" or "rsa-sha2-512" => EstimateRsaKeySize(hostKey),
                "ssh-dss" => 1024, // Standard DSA size

                _ => 0 // Unknown key type
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Estimates RSA key size from the public key data.
    /// RSA public keys contain the modulus (n) which determines the key size.
    /// </summary>
    private int EstimateRsaKeySize(byte[] hostKey)
    {
        try
        {
            // SSH RSA public key format:
            // - 4 bytes: length of "ssh-rsa" string
            // - N bytes: "ssh-rsa" string
            // - 4 bytes: length of exponent (e)
            // - M bytes: exponent
            // - 4 bytes: length of modulus (n)
            // - K bytes: modulus

            int offset = 0;

            // Skip key type string
            if (hostKey.Length < offset + 4) return 0;
            int typeLength = ReadInt32BigEndian(hostKey, offset);
            offset += 4 + typeLength;

            // Skip exponent
            if (hostKey.Length < offset + 4) return 0;
            int exponentLength = ReadInt32BigEndian(hostKey, offset);
            offset += 4 + exponentLength;

            // Read modulus length
            if (hostKey.Length < offset + 4) return 0;
            int modulusLength = ReadInt32BigEndian(hostKey, offset);

            // RSA key size is the bit length of the modulus
            // modulusLength is in bytes, so multiply by 8 for bits
            return modulusLength * 8;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reads a 32-bit big-endian integer from a byte array at the specified offset.
    /// </summary>
    private int ReadInt32BigEndian(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }

    /// <summary>
    /// Internal result type for agent scanning operations.
    /// </summary>
    private record AgentScanResult(bool Available, List<AgentKeyInfo> Keys);
}
