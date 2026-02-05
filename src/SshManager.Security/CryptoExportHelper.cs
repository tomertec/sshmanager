using System.Security.Cryptography;
using System.Text;

namespace SshManager.Security;

/// <summary>
/// Internal helper for exporting cryptographic keys in PEM format.
/// Shared by SshKeyManagerService and KeyEncryptionService.
/// </summary>
internal static class CryptoExportHelper
{
    /// <summary>
    /// Exports an RSA private key in PKCS#8 format, optionally encrypted.
    /// </summary>
    /// <param name="rsa">The RSA key to export.</param>
    /// <param name="passphrase">Optional passphrase for encryption. If null or empty, the key is exported unencrypted.</param>
    /// <returns>PEM-encoded private key string.</returns>
    public static string ExportRsaPrivateKey(RSA rsa, string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            // Export unencrypted in PKCS#8 format
            var privateKeyBytes = rsa.ExportPkcs8PrivateKey();
            return FormatPem(privateKeyBytes, "PRIVATE KEY");
        }
        else
        {
            // Export encrypted with AES-256-CBC (standard for SSH keys)
            var pbeParams = new PbeParameters(
                PbeEncryptionAlgorithm.Aes256Cbc,
                HashAlgorithmName.SHA256,
                100000);
            var privateKeyBytes = rsa.ExportEncryptedPkcs8PrivateKey(
                Encoding.UTF8.GetBytes(passphrase),
                pbeParams);
            return FormatPem(privateKeyBytes, "ENCRYPTED PRIVATE KEY");
        }
    }

    /// <summary>
    /// Exports an ECDSA private key in PKCS#8 format, optionally encrypted.
    /// </summary>
    /// <param name="ecdsa">The ECDSA key to export.</param>
    /// <param name="passphrase">Optional passphrase for encryption. If null or empty, the key is exported unencrypted.</param>
    /// <returns>PEM-encoded private key string.</returns>
    public static string ExportEcdsaPrivateKey(ECDsa ecdsa, string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            // Export unencrypted in PKCS#8 format
            var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
            return FormatPem(privateKeyBytes, "PRIVATE KEY");
        }
        else
        {
            // Export encrypted with AES-256-CBC
            var pbeParams = new PbeParameters(
                PbeEncryptionAlgorithm.Aes256Cbc,
                HashAlgorithmName.SHA256,
                100000);
            var privateKeyBytes = ecdsa.ExportEncryptedPkcs8PrivateKey(
                Encoding.UTF8.GetBytes(passphrase),
                pbeParams);
            return FormatPem(privateKeyBytes, "ENCRYPTED PRIVATE KEY");
        }
    }

    /// <summary>
    /// Formats key bytes as PEM with proper headers and Base64 encoding.
    /// Lines are wrapped at 64 characters as per PEM standard.
    /// </summary>
    /// <param name="data">The binary key data to encode.</param>
    /// <param name="label">The PEM label (e.g., "PRIVATE KEY" or "ENCRYPTED PRIVATE KEY").</param>
    /// <returns>PEM-formatted string with headers, Base64-encoded data, and footers.</returns>
    public static string FormatPem(byte[] data, string label)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-----BEGIN {label}-----");

        var base64 = Convert.ToBase64String(data);
        for (int i = 0; i < base64.Length; i += 64)
        {
            sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        sb.AppendLine($"-----END {label}-----");
        return sb.ToString();
    }
}
