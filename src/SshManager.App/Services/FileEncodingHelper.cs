using System.Security.Cryptography;
using System.Text;

namespace SshManager.App.Services;

/// <summary>
/// Shared utilities for file encoding detection and content hashing.
/// </summary>
internal static class FileEncodingHelper
{
    /// <summary>
    /// Detects encoding from BOM bytes, defaulting to UTF-8 without BOM.
    /// </summary>
    public static Encoding DetectEncoding(byte[] content)
    {
        if (content.Length >= 3 &&
            content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        if (content.Length >= 2)
        {
            if (content[0] == 0xFE && content[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            if (content[0] == 0xFF && content[1] == 0xFE)
                return Encoding.Unicode; // UTF-16 LE
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    /// <summary>
    /// Computes a SHA256 hash of byte content and returns it as a hex string.
    /// </summary>
    public static string ComputeHash(byte[] content)
    {
        var hashBytes = SHA256.HashData(content);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Computes a SHA256 hash of string content (using UTF-8) and returns it as a hex string.
    /// </summary>
    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
}
