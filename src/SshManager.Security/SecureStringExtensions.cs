using System.Runtime.InteropServices;
using System.Security;

namespace SshManager.Security;

/// <summary>
/// Extension methods for working with SecureString.
/// </summary>
public static class SecureStringExtensions
{
    /// <summary>
    /// Converts a regular string to a SecureString.
    /// </summary>
    /// <param name="plainText">The plain text to convert.</param>
    /// <returns>A new SecureString containing the text, or null if input is null.</returns>
    public static SecureString? ToSecureString(this string? plainText)
    {
        if (plainText == null)
            return null;

        var secureString = new SecureString();
        foreach (var c in plainText)
        {
            secureString.AppendChar(c);
        }
        secureString.MakeReadOnly();
        return secureString;
    }

    /// <summary>
    /// Converts a SecureString back to a regular string.
    /// WARNING: This creates an unprotected copy of the sensitive data in memory.
    /// Use sparingly and clear the result as soon as possible.
    /// </summary>
    /// <param name="secureString">The SecureString to convert.</param>
    /// <returns>The plain text value, or null if input is null.</returns>
    public static string? ToUnsecureString(this SecureString? secureString)
    {
        if (secureString == null)
            return null;

        IntPtr unmanagedString = IntPtr.Zero;
        try
        {
            unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            return Marshal.PtrToStringUni(unmanagedString);
        }
        finally
        {
            if (unmanagedString != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }
    }

    /// <summary>
    /// Creates a copy of a SecureString without exposing the value as a plain string.
    /// </summary>
    /// <param name="secureString">The SecureString to copy.</param>
    /// <returns>A new SecureString with the same content, or null if input is null or empty.</returns>
    public static SecureString? Copy(this SecureString? secureString)
    {
        if (secureString == null || secureString.Length == 0)
            return null;

        var copy = new SecureString();
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToBSTR(secureString);
            for (int i = 0; i < secureString.Length; i++)
            {
                copy.AppendChar((char)Marshal.ReadInt16(ptr, i * 2));
            }
            copy.MakeReadOnly();
            return copy;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
                Marshal.ZeroFreeBSTR(ptr);
        }
    }

    /// <summary>
    /// Securely clears and disposes a SecureString.
    /// </summary>
    /// <param name="secureString">The SecureString to clear.</param>
    public static void ClearAndDispose(this SecureString? secureString)
    {
        if (secureString == null)
            return;

        secureString.Clear();
        secureString.Dispose();
    }
}
