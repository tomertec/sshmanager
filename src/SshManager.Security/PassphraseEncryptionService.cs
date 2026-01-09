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
    // AES-GCM parameters
    private const int SaltSize = 16;      // 128 bits
    private const int KeySize = 32;       // 256 bits
    private const int NonceSize = 12;     // 96 bits (GCM standard)
    private const int TagSize = 16;       // 128 bits

    // Default Argon2id parameters (OWASP recommended)
    private const int DefaultMemorySize = 65536;    // 64 MB
    private const int DefaultIterations = 3;
    private const int DefaultParallelism = 4;

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

        _logger.LogDebug("Encrypting data ({Length} bytes)", plaintext.Length);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var kdfParams = new Argon2Parameters
        {
            MemorySize = DefaultMemorySize,
            Iterations = DefaultIterations,
            Parallelism = DefaultParallelism
        };

        var key = DeriveKey(passphrase, salt, kdfParams);

        try
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Combine: nonce || ciphertext || tag
            var combined = new byte[NonceSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, combined, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, NonceSize + ciphertext.Length, TagSize);

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
            // Securely clear the key from memory
            CryptographicOperations.ZeroMemory(key);
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

        var salt = Convert.FromBase64String(data.Salt);
        var combined = Convert.FromBase64String(data.Data);
        var key = DeriveKey(passphrase, salt, data.KdfParameters);

        try
        {
            // Extract: nonce || ciphertext || tag
            if (combined.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("Invalid encrypted data format");
            }

            var nonce = new byte[NonceSize];
            var ciphertext = new byte[combined.Length - NonceSize - TagSize];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(combined, NonceSize, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(combined, NonceSize + ciphertext.Length, tag, 0, TagSize);

            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            var result = Encoding.UTF8.GetString(plaintext);
            _logger.LogDebug("Data decrypted successfully ({Length} bytes)", result.Length);
            return result;
        }
        finally
        {
            // Securely clear the key from memory
            CryptographicOperations.ZeroMemory(key);
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
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            MemorySize = parameters.MemorySize,
            Iterations = parameters.Iterations,
            DegreeOfParallelism = parameters.Parallelism
        };

        return argon2.GetBytes(KeySize);
    }
}
