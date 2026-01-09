namespace SshManager.Security;

/// <summary>
/// Service for encrypting and decrypting data using a user-provided passphrase.
/// Uses AES-256-GCM for encryption with Argon2id for key derivation.
/// </summary>
public interface IPassphraseEncryptionService
{
    /// <summary>
    /// Encrypts plaintext data using AES-256-GCM with Argon2id key derivation.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="passphrase">The user's passphrase for encryption.</param>
    /// <returns>Encrypted data with all necessary metadata for decryption.</returns>
    EncryptedSyncData Encrypt(string plaintext, string passphrase);

    /// <summary>
    /// Decrypts data using the provided passphrase.
    /// </summary>
    /// <param name="data">The encrypted data with metadata.</param>
    /// <param name="passphrase">The user's passphrase for decryption.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when decryption fails (wrong passphrase or tampered data).
    /// </exception>
    string Decrypt(EncryptedSyncData data, string passphrase);

    /// <summary>
    /// Verifies if the passphrase can successfully decrypt the data.
    /// </summary>
    /// <param name="data">The encrypted data to verify against.</param>
    /// <param name="passphrase">The passphrase to verify.</param>
    /// <returns>True if the passphrase is correct; otherwise, false.</returns>
    bool VerifyPassphrase(EncryptedSyncData data, string passphrase);
}
