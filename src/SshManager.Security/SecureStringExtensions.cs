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
    /// <remarks>
    /// <para><strong>SECURITY LIMITATION:</strong> This method accepts a plain <see cref="string"/> parameter,
    /// which means the sensitive data already exists in memory as an unprotected, immutable string before
    /// conversion to <see cref="SecureString"/>. The plain string remains in memory until garbage collected,
    /// and because strings are immutable in .NET, it cannot be zeroed out or securely cleared.</para>
    ///
    /// <para><strong>Memory Security Risk:</strong> The original string can potentially be:</para>
    /// <list type="bullet">
    /// <item><description>Captured in memory dumps or crash dumps</description></item>
    /// <item><description>Exposed through debugging tools</description></item>
    /// <item><description>Copied during garbage collection compaction</description></item>
    /// <item><description>Swapped to disk if system memory is under pressure</description></item>
    /// </list>
    ///
    /// <para><strong>When This Is Acceptable:</strong></para>
    /// <list type="bullet">
    /// <item><description>Converting already-loaded strings from configuration files or databases</description></item>
    /// <item><description>Working with data that was already exposed as a plain string elsewhere in the codebase</description></item>
    /// <item><description>Integrating with APIs that only provide string-based credentials</description></item>
    /// <item><description>The sensitive data has a short lifetime in memory before conversion</description></item>
    /// </list>
    ///
    /// <para><strong>When To Use Character-by-Character Input Instead:</strong></para>
    /// <list type="bullet">
    /// <item><description>User input from password fields (use <see cref="System.Windows.Controls.PasswordBox"/> in WPF)</description></item>
    /// <item><description>Console applications reading passwords (use Console.ReadKey in a loop)</description></item>
    /// <item><description>Any scenario where sensitive data can be directly appended to SecureString without creating a plain string first</description></item>
    /// </list>
    ///
    /// <para><strong>Best Practice:</strong> After calling this method, set the source string reference to <c>null</c>
    /// as soon as possible to make it eligible for garbage collection. While this doesn't immediately remove the string
    /// from memory, it reduces the window of exposure.</para>
    ///
    /// <example>
    /// <code>
    /// string password = GetPasswordFromSomewhere();
    /// SecureString securePassword = password.ToSecureString();
    /// password = null; // Allow GC to reclaim the plain string
    ///
    /// // Use securePassword...
    /// securePassword.ClearAndDispose();
    /// </code>
    /// </example>
    /// </remarks>
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
