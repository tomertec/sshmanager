using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Security;

/// <summary>
/// Implements passphrase-based encryption using AES-256-GCM with Argon2id key derivation.
/// Follows OWASP recommendations for secure password-based encryption.
/// </summary>
public class PassphraseEncryptionService : IPassphraseEncryptionService
{
    private readonly ILogger<PassphraseEncryptionService> _logger;

    public PassphraseEncryptionService(ILogger<PassphraseEncryptionService>? logger = null)
    {
        _logger = logger ?? NullLogger<PassphraseEncryptionService>.Instance;
    }

    /// <inheritdoc />
    public EncryptedSyncData Encrypt(string plaintext, string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        _logger.LogDebug("Encrypting data");

        byte[]? plaintextBytes = null;
        byte[]? nonce = null;
        byte[]? key = null;
        byte[]? salt = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? combined = null;

        try
        {
            salt = RandomNumberGenerator.GetBytes(SecurityConstants.PassphraseEncryption.SaltSize);
            var kdfParams = new Argon2Parameters
            {
                MemorySize = SecurityConstants.PassphraseEncryption.DefaultMemorySize,
                Iterations = SecurityConstants.PassphraseEncryption.DefaultIterations,
                Parallelism = SecurityConstants.PassphraseEncryption.DefaultParallelism
            };

            key = DeriveKey(passphrase, salt, kdfParams);

            plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            nonce = RandomNumberGenerator.GetBytes(SecurityConstants.PassphraseEncryption.NonceSize);
            ciphertext = new byte[plaintextBytes.Length];
            tag = new byte[SecurityConstants.PassphraseEncryption.TagSize];

            using var aes = new AesGcm(key, SecurityConstants.PassphraseEncryption.TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Combine: nonce || ciphertext || tag
            combined = new byte[SecurityConstants.PassphraseEncryption.NonceSize + ciphertext.Length + SecurityConstants.PassphraseEncryption.TagSize];
            Buffer.BlockCopy(nonce, 0, combined, 0, SecurityConstants.PassphraseEncryption.NonceSize);
            Buffer.BlockCopy(ciphertext, 0, combined, SecurityConstants.PassphraseEncryption.NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, SecurityConstants.PassphraseEncryption.NonceSize + ciphertext.Length, SecurityConstants.PassphraseEncryption.TagSize);

            var result = new EncryptedSyncData
            {
                Version = 1,
                Algorithm = "AES-256-GCM",
                KdfAlgorithm = "Argon2id",
                KdfParameters = kdfParams,
                Salt = Convert.ToBase64String(salt),
                Data = Convert.ToBase64String(combined),
                ModifiedAt = DateTimeOffset.UtcNow
            };

            _logger.LogDebug("Data encrypted successfully");
            return result;
        }
        finally
        {
            // Securely clear all sensitive data from memory
            if (plaintextBytes != null) CryptographicOperations.ZeroMemory(plaintextBytes);
            if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
            if (key != null) CryptographicOperations.ZeroMemory(key);
            if (salt != null) CryptographicOperations.ZeroMemory(salt);
            if (ciphertext != null) CryptographicOperations.ZeroMemory(ciphertext);
            if (tag != null) CryptographicOperations.ZeroMemory(tag);
            if (combined != null) CryptographicOperations.ZeroMemory(combined);
        }
    }

    /// <inheritdoc />
    public string Decrypt(EncryptedSyncData data, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        if (data.Version != 1)
        {
            throw new NotSupportedException($"Unsupported encryption version: {data.Version}");
        }

        if (data.Algorithm != "AES-256-GCM")
        {
            throw new NotSupportedException($"Unsupported encryption algorithm: {data.Algorithm}");
        }

        _logger.LogDebug("Decrypting data");

        byte[]? salt = null;
        byte[]? combined = null;
        byte[]? key = null;
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? plaintextBytes = null;

        try
        {
            salt = Convert.FromBase64String(data.Salt);
            combined = Convert.FromBase64String(data.Data);
            key = DeriveKey(passphrase, salt, data.KdfParameters);

            // Extract: nonce || ciphertext || tag
            if (combined.Length < SecurityConstants.PassphraseEncryption.NonceSize + SecurityConstants.PassphraseEncryption.TagSize)
            {
                throw new CryptographicException("Invalid encrypted data format");
            }

            nonce = new byte[SecurityConstants.PassphraseEncryption.NonceSize];
            ciphertext = new byte[combined.Length - SecurityConstants.PassphraseEncryption.NonceSize - SecurityConstants.PassphraseEncryption.TagSize];
            tag = new byte[SecurityConstants.PassphraseEncryption.TagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, SecurityConstants.PassphraseEncryption.NonceSize);
            Buffer.BlockCopy(combined, SecurityConstants.PassphraseEncryption.NonceSize, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(combined, SecurityConstants.PassphraseEncryption.NonceSize + ciphertext.Length, tag, 0, SecurityConstants.PassphraseEncryption.TagSize);

            plaintextBytes = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, SecurityConstants.PassphraseEncryption.TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

            var result = Encoding.UTF8.GetString(plaintextBytes);
            _logger.LogDebug("Data decrypted successfully");
            return result;
        }
        finally
        {
            // Securely clear all sensitive data from memory
            if (salt != null) CryptographicOperations.ZeroMemory(salt);
            if (combined != null) CryptographicOperations.ZeroMemory(combined);
            if (key != null) CryptographicOperations.ZeroMemory(key);
            if (nonce != null) CryptographicOperations.ZeroMemory(nonce);
            if (ciphertext != null) CryptographicOperations.ZeroMemory(ciphertext);
            if (tag != null) CryptographicOperations.ZeroMemory(tag);
            if (plaintextBytes != null) CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <inheritdoc />
    public bool VerifyPassphrase(EncryptedSyncData data, string passphrase)
    {
        try
        {
            Decrypt(data, passphrase);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during passphrase verification");
            return false;
        }
    }

    /// <summary>
    /// Derives an encryption key from a passphrase using Argon2id.
    /// </summary>
    private byte[] DeriveKey(string passphrase, byte[] salt, Argon2Parameters parameters)
    {
        byte[]? passphraseBytes = null;
        try
        {
            passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
            using var argon2 = new Argon2id(passphraseBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemorySize,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism
            };

            return argon2.GetBytes(SecurityConstants.PassphraseEncryption.KeySize);
        }
        finally
        {
            // Securely clear passphrase bytes from memory
            if (passphraseBytes != null) CryptographicOperations.ZeroMemory(passphraseBytes);
        }
    }
}
