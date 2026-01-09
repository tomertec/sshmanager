namespace SshManager.Security;

/// <summary>
/// Interface for encrypting and decrypting sensitive data.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Encrypts plaintext and returns a base64-encoded protected string.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a base64-encoded protected string back to plaintext.
    /// </summary>
    string Unprotect(string protectedBase64);

    /// <summary>
    /// Attempts to decrypt a protected string, returning null on failure instead of throwing.
    /// </summary>
    string? TryUnprotect(string? protectedBase64);
}
