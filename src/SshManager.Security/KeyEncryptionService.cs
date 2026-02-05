using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;

namespace SshManager.Security;

/// <summary>
/// Service for re-encrypting SSH private keys with passphrases.
/// Supports RSA and ECDSA keys in PKCS#8 format.
/// </summary>
public sealed class KeyEncryptionService : IKeyEncryptionService
{
    private readonly ILogger<KeyEncryptionService> _logger;

    public KeyEncryptionService(ILogger<KeyEncryptionService>? logger = null)
    {
        _logger = logger ?? NullLogger<KeyEncryptionService>.Instance;
    }

    /// <inheritdoc />
    public async Task<KeyEncryptionResult> EncryptKeyAsync(
        string privateKeyPath,
        string newPassphrase,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyPath);
        ArgumentException.ThrowIfNullOrEmpty(newPassphrase);

        _logger.LogInformation("Encrypting key at {Path}", privateKeyPath);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(privateKeyPath))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        $"Key file not found: {privateKeyPath}");
                }

                var originalContent = File.ReadAllText(privateKeyPath);

                if (IsKeyContentEncrypted(originalContent))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        "Key is already encrypted. Use ChangePassphraseAsync to change the passphrase.");
                }

                // Validate the key can be loaded first
                if (!ValidateKey(privateKeyPath, null))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        "Cannot load the key. It may be corrupted or in an unsupported format.");
                }

                // Create backup
                var backupPath = CreateBackup(privateKeyPath);
                _logger.LogDebug("Created backup at {BackupPath}", backupPath);

                try
                {
                    // Re-encrypt the key
                    var encryptedContent = ReencryptKey(originalContent, null, newPassphrase);

                    // Validate the newly encrypted key can be loaded
                    if (!ValidateKeyContent(encryptedContent, newPassphrase))
                    {
                        throw new InvalidOperationException(
                            "Validation of encrypted key failed. The key may not have been encrypted correctly.");
                    }

                    // Save the encrypted key
                    File.WriteAllText(privateKeyPath, encryptedContent);

                    _logger.LogInformation("Successfully encrypted key at {Path}", privateKeyPath);

                    return new KeyEncryptionResult(true, encryptedContent, null);
                }
                catch (Exception ex)
                {
                    // Restore from backup on failure
                    _logger.LogWarning(ex, "Failed to encrypt key, restoring from backup");
                    RestoreFromBackup(privateKeyPath, backupPath);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encrypting key");
                return new KeyEncryptionResult(false, null, ex.Message);
            }
        }, ct);
    }

    /// <inheritdoc />
    public async Task<KeyEncryptionResult> ChangePassphraseAsync(
        string privateKeyPath,
        string oldPassphrase,
        string newPassphrase,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyPath);
        ArgumentException.ThrowIfNullOrEmpty(oldPassphrase);
        ArgumentException.ThrowIfNullOrEmpty(newPassphrase);

        _logger.LogInformation("Changing passphrase for key at {Path}", privateKeyPath);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(privateKeyPath))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        $"Key file not found: {privateKeyPath}");
                }

                var originalContent = File.ReadAllText(privateKeyPath);

                if (!IsKeyContentEncrypted(originalContent))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        "Key is not encrypted. Use EncryptKeyAsync to encrypt it.");
                }

                // Validate the key can be loaded with the old passphrase
                if (!ValidateKey(privateKeyPath, oldPassphrase))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        "Cannot load the key with the provided passphrase. The passphrase may be incorrect.");
                }

                // Create backup
                var backupPath = CreateBackup(privateKeyPath);
                _logger.LogDebug("Created backup at {BackupPath}", backupPath);

                try
                {
                    // Re-encrypt with new passphrase
                    var encryptedContent = ReencryptKey(originalContent, oldPassphrase, newPassphrase);

                    // Validate the newly encrypted key can be loaded
                    if (!ValidateKeyContent(encryptedContent, newPassphrase))
                    {
                        throw new InvalidOperationException(
                            "Validation of re-encrypted key failed. The key may not have been encrypted correctly.");
                    }

                    // Save the re-encrypted key
                    File.WriteAllText(privateKeyPath, encryptedContent);

                    _logger.LogInformation("Successfully changed passphrase for key at {Path}", privateKeyPath);

                    return new KeyEncryptionResult(true, encryptedContent, null);
                }
                catch (Exception ex)
                {
                    // Restore from backup on failure
                    _logger.LogWarning(ex, "Failed to change passphrase, restoring from backup");
                    RestoreFromBackup(privateKeyPath, backupPath);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing passphrase");
                return new KeyEncryptionResult(false, null, ex.Message);
            }
        }, ct);
    }

    /// <inheritdoc />
    public async Task<KeyEncryptionResult> DecryptKeyAsync(
        string privateKeyPath,
        string passphrase,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyPath);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        _logger.LogInformation("Decrypting key at {Path}", privateKeyPath);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(privateKeyPath))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        $"Key file not found: {privateKeyPath}");
                }

                var originalContent = File.ReadAllText(privateKeyPath);

                if (!IsKeyContentEncrypted(originalContent))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        "Key is already unencrypted.");
                }

                // Validate the key can be loaded with the passphrase
                if (!ValidateKey(privateKeyPath, passphrase))
                {
                    return new KeyEncryptionResult(
                        false,
                        null,
                        "Cannot load the key with the provided passphrase. The passphrase may be incorrect.");
                }

                // Create backup
                var backupPath = CreateBackup(privateKeyPath);
                _logger.LogDebug("Created backup at {BackupPath}", backupPath);

                try
                {
                    // Decrypt the key (re-encrypt with no passphrase)
                    var decryptedContent = ReencryptKey(originalContent, passphrase, null);

                    // Validate the decrypted key can be loaded
                    if (!ValidateKeyContent(decryptedContent, null))
                    {
                        throw new InvalidOperationException(
                            "Validation of decrypted key failed. The key may not have been decrypted correctly.");
                    }

                    // Save the decrypted key
                    File.WriteAllText(privateKeyPath, decryptedContent);

                    _logger.LogInformation("Successfully decrypted key at {Path}", privateKeyPath);

                    return new KeyEncryptionResult(true, decryptedContent, null);
                }
                catch (Exception ex)
                {
                    // Restore from backup on failure
                    _logger.LogWarning(ex, "Failed to decrypt key, restoring from backup");
                    RestoreFromBackup(privateKeyPath, backupPath);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrypting key");
                return new KeyEncryptionResult(false, null, ex.Message);
            }
        }, ct);
    }

    /// <inheritdoc />
    public async Task<string> EncryptKeyContentAsync(
        string privateKeyContent,
        string passphrase,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyContent);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        _logger.LogDebug("Encrypting key content");

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (IsKeyContentEncrypted(privateKeyContent))
            {
                throw new InvalidOperationException("Key content is already encrypted.");
            }

            return ReencryptKey(privateKeyContent, null, passphrase);
        }, ct);
    }

    /// <inheritdoc />
    public bool IsKeyEncrypted(string privateKeyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyPath);

        if (!File.Exists(privateKeyPath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(privateKeyPath);
            return IsKeyContentEncrypted(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading key file to check encryption status");
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsKeyContentEncrypted(string privateKeyContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyContent);

        // PKCS#8 encrypted keys have "ENCRYPTED PRIVATE KEY" header
        if (privateKeyContent.Contains("ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for OpenSSH format encrypted keys
        if (privateKeyContent.Contains("OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            // Try to parse to determine if encrypted
            // Unencrypted OpenSSH keys have "none" as cipher and kdf
            // Encrypted ones typically use "aes256-ctr" or similar with "bcrypt" kdf
            try
            {
                // Quick heuristic: check if the key can be loaded without a passphrase
                // If it requires a passphrase, SSH.NET will throw
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKeyContent));
                try
                {
                    using var _ = new PrivateKeyFile(ms);
                    // If we got here, it's unencrypted
                    return false;
                }
                catch (Renci.SshNet.Common.SshPassPhraseNullOrEmptyException)
                {
                    // SSH.NET detected the key is encrypted and requires a passphrase
                    return true;
                }
                catch
                {
                    // Some other error - assume it might be encrypted
                    return true;
                }
            }
            catch
            {
                // If we can't determine, assume not encrypted (safer default)
                return false;
            }
        }

        return false;
    }

    #region Private Helper Methods

    /// <summary>
    /// Re-encrypts a key from one passphrase to another (or to/from unencrypted).
    /// </summary>
    private string ReencryptKey(string keyContent, string? oldPassphrase, string? newPassphrase)
    {
        // Try to determine key type and re-export
        // First, try RSA
        try
        {
            return ReencryptRsaKey(keyContent, oldPassphrase, newPassphrase);
        }
        catch
        {
            // Not RSA or failed, try ECDSA
        }

        try
        {
            return ReencryptEcdsaKey(keyContent, oldPassphrase, newPassphrase);
        }
        catch
        {
            // Not ECDSA either
        }

        // For Ed25519 and other OpenSSH format keys, we can't easily re-encrypt with .NET APIs
        // SSH.NET can read them but doesn't expose the raw key material in a way we can re-export
        throw new NotSupportedException(
            "This key format is not supported for re-encryption. Only RSA and ECDSA keys in PKCS#8 format are supported.");
    }

    /// <summary>
    /// Re-encrypts an RSA key.
    /// </summary>
    private static string ReencryptRsaKey(string keyContent, string? oldPassphrase, string? newPassphrase)
    {
        // Import the key
        using var rsa = RSA.Create();

        var keyBytes = ExtractPemBytes(keyContent);
        if (string.IsNullOrEmpty(oldPassphrase))
        {
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);
        }
        else
        {
            rsa.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(oldPassphrase), keyBytes, out _);
        }

        // Export with new passphrase
        return ExportRsaPrivateKey(rsa, newPassphrase);
    }

    /// <summary>
    /// Re-encrypts an ECDSA key.
    /// </summary>
    private static string ReencryptEcdsaKey(string keyContent, string? oldPassphrase, string? newPassphrase)
    {
        // Import the key
        using var ecdsa = ECDsa.Create();

        var keyBytes = ExtractPemBytes(keyContent);
        if (string.IsNullOrEmpty(oldPassphrase))
        {
            ecdsa.ImportPkcs8PrivateKey(keyBytes, out _);
        }
        else
        {
            ecdsa.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(oldPassphrase), keyBytes, out _);
        }

        // Export with new passphrase
        return ExportEcdsaPrivateKey(ecdsa, newPassphrase);
    }

    /// <summary>
    /// Exports an RSA private key in PKCS#8 format, optionally encrypted.
    /// </summary>
    private static string ExportRsaPrivateKey(RSA rsa, string? passphrase)
    {
        return CryptoExportHelper.ExportRsaPrivateKey(rsa, passphrase);
    }

    /// <summary>
    /// Exports an ECDSA private key in PKCS#8 format, optionally encrypted.
    /// </summary>
    private static string ExportEcdsaPrivateKey(ECDsa ecdsa, string? passphrase)
    {
        return CryptoExportHelper.ExportEcdsaPrivateKey(ecdsa, passphrase);
    }

    /// <summary>
    /// Formats key bytes as PEM with proper headers and Base64 encoding.
    /// </summary>
    private static string FormatPem(byte[] data, string label)
    {
        return CryptoExportHelper.FormatPem(data, label);
    }

    /// <summary>
    /// Extracts the Base64-encoded bytes from a PEM formatted key.
    /// </summary>
    private static byte[] ExtractPemBytes(string pem)
    {
        var lines = pem.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("-----"))
            .Where(l => !string.IsNullOrWhiteSpace(l));

        var base64 = string.Join("", lines);
        return Convert.FromBase64String(base64);
    }

    /// <summary>
    /// Validates that a key file can be loaded with SSH.NET.
    /// </summary>
    private bool ValidateKey(string privateKeyPath, string? passphrase)
    {
        try
        {
            using var keyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Key validation failed");
            return false;
        }
    }

    /// <summary>
    /// Validates that key content can be loaded with SSH.NET.
    /// </summary>
    private bool ValidateKeyContent(string keyContent, string? passphrase)
    {
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(keyContent));
            using var keyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(stream)
                : new PrivateKeyFile(stream, passphrase);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Key content validation failed");
            return false;
        }
    }

    /// <summary>
    /// Creates a backup of the key file.
    /// </summary>
    private string CreateBackup(string privateKeyPath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = $"{privateKeyPath}.backup.{timestamp}";

        File.Copy(privateKeyPath, backupPath, overwrite: true);

        return backupPath;
    }

    /// <summary>
    /// Restores a key file from backup.
    /// </summary>
    private void RestoreFromBackup(string privateKeyPath, string backupPath)
    {
        try
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, privateKeyPath, overwrite: true);
                _logger.LogInformation("Restored key from backup: {BackupPath}", backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore from backup");
        }
    }

    #endregion
}
