namespace SshManager.Security;

/// <summary>
/// Service for re-encrypting SSH private keys with passphrases.
/// Supports encrypting unencrypted keys, changing passphrases, and removing encryption.
/// </summary>
public interface IKeyEncryptionService
{
    /// <summary>
    /// Encrypts an unencrypted private key with a passphrase.
    /// Creates a backup of the original key before modification.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="newPassphrase">The passphrase to encrypt the key with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or failure with error details.</returns>
    Task<KeyEncryptionResult> EncryptKeyAsync(
        string privateKeyPath,
        string newPassphrase,
        CancellationToken ct = default);

    /// <summary>
    /// Changes the passphrase of an encrypted key.
    /// Creates a backup of the original key before modification.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="oldPassphrase">The current passphrase.</param>
    /// <param name="newPassphrase">The new passphrase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or failure with error details.</returns>
    Task<KeyEncryptionResult> ChangePassphraseAsync(
        string privateKeyPath,
        string oldPassphrase,
        string newPassphrase,
        CancellationToken ct = default);

    /// <summary>
    /// Removes encryption from a key, making it unencrypted.
    /// Creates a backup of the original key before modification.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="passphrase">The current passphrase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or failure with error details.</returns>
    Task<KeyEncryptionResult> DecryptKeyAsync(
        string privateKeyPath,
        string passphrase,
        CancellationToken ct = default);

    /// <summary>
    /// Encrypts key content in memory without file operations.
    /// Useful for converting unencrypted key content to encrypted format.
    /// </summary>
    /// <param name="privateKeyContent">The private key content in PEM format.</param>
    /// <param name="passphrase">The passphrase to encrypt the key with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encrypted key content in PEM format.</returns>
    /// <exception cref="InvalidOperationException">Thrown when key format is not supported.</exception>
    Task<string> EncryptKeyContentAsync(
        string privateKeyContent,
        string passphrase,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a private key file is encrypted.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <returns>True if the key is encrypted; otherwise, false.</returns>
    bool IsKeyEncrypted(string privateKeyPath);

    /// <summary>
    /// Checks if key content is encrypted.
    /// </summary>
    /// <param name="privateKeyContent">The private key content in PEM format.</param>
    /// <returns>True if the key content is encrypted; otherwise, false.</returns>
    bool IsKeyContentEncrypted(string privateKeyContent);
}

/// <summary>
/// Result of a key encryption operation.
/// </summary>
/// <param name="Success">Whether the operation was successful.</param>
/// <param name="NewKeyContent">The new encrypted/decrypted key content (null on failure).</param>
/// <param name="ErrorMessage">Error message if the operation failed (null on success).</param>
public record KeyEncryptionResult(
    bool Success,
    string? NewKeyContent,
    string? ErrorMessage);
