namespace SshManager.Security;

/// <summary>
/// Service for managing SSH keys - generation, listing, and validation.
/// </summary>
public interface ISshKeyManager
{
    /// <summary>
    /// Generates a new SSH key pair.
    /// </summary>
    /// <param name="keyType">The type of key to generate.</param>
    /// <param name="passphrase">Optional passphrase to encrypt the private key.</param>
    /// <param name="comment">Optional comment to embed in the key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated key pair.</returns>
    Task<SshKeyPair> GenerateKeyAsync(SshKeyType keyType, string? passphrase = null, string? comment = null, CancellationToken ct = default);

    /// <summary>
    /// Gets information about existing SSH keys in the user's .ssh directory.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of existing key information.</returns>
    Task<IReadOnlyList<SshKeyInfo>> GetExistingKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the public key from a private key file.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="passphrase">Passphrase if the key is encrypted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public key in OpenSSH format.</returns>
    Task<string> GetPublicKeyAsync(string privateKeyPath, string? passphrase = null, CancellationToken ct = default);

    /// <summary>
    /// Saves a key pair to files.
    /// </summary>
    /// <param name="keyPair">The key pair to save.</param>
    /// <param name="privateKeyPath">Path for the private key file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveKeyPairAsync(SshKeyPair keyPair, string privateKeyPath, CancellationToken ct = default);

    /// <summary>
    /// Validates that a private key file is valid and optionally verifies the passphrase.
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="passphrase">Passphrase to verify (null to just check if key is valid/unencrypted).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the key is valid and passphrase matches (if provided).</returns>
    Task<bool> ValidateKeyAsync(string privateKeyPath, string? passphrase = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a key pair (both private and public key files).
    /// </summary>
    /// <param name="privateKeyPath">Path to the private key file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteKeyAsync(string privateKeyPath, CancellationToken ct = default);

    /// <summary>
    /// Gets the default SSH directory path (~/.ssh).
    /// </summary>
    string GetDefaultSshDirectory();

    /// <summary>
    /// Computes the SHA256 fingerprint of a public key.
    /// </summary>
    /// <param name="publicKey">The public key in OpenSSH format.</param>
    /// <returns>The fingerprint in SHA256:base64 format.</returns>
    string ComputeFingerprint(string publicKey);
}
