namespace SshManager.Core.Models;

/// <summary>
/// Types of remote shell environments for determining how to apply environment variables.
/// </summary>
/// <remarks>
/// <para>
/// Environment variables are typically set via shell commands (e.g., <c>export VAR="value"</c>).
/// Different shell types require different syntax, and some don't support environment variables at all.
/// </para>
/// <para>
/// When <see cref="Auto"/> is selected, the application assumes POSIX-compliant shells for SSH connections.
/// For network appliances or Windows SSH servers, explicitly select the appropriate type to either
/// use the correct syntax or skip environment variable application entirely.
/// </para>
/// </remarks>
public enum ShellType
{
    /// <summary>
    /// Automatically detect shell type (default). Assumes POSIX-compliant shell for SSH connections.
    /// Environment variables are applied using <c>export VAR="value"</c> syntax.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// POSIX-compliant shell (bash, zsh, sh, ash, dash, etc.).
    /// Environment variables are applied using <c>export VAR="value"</c> syntax.
    /// </summary>
    Posix = 1,

    /// <summary>
    /// Windows PowerShell or PowerShell Core.
    /// Environment variables would use <c>$env:VAR = "value"</c> syntax.
    /// Currently, environment variables are skipped for PowerShell to avoid command errors.
    /// </summary>
    PowerShell = 2,

    /// <summary>
    /// Windows Command Prompt (CMD).
    /// Environment variables would use <c>set VAR=value</c> syntax.
    /// Currently, environment variables are skipped for CMD to avoid command errors.
    /// </summary>
    Cmd = 3,

    /// <summary>
    /// Network appliance CLI (Cisco IOS, Juniper JunOS, Arista EOS, etc.).
    /// These CLIs do not support environment variables; they are skipped entirely.
    /// </summary>
    NetworkAppliance = 4,

    /// <summary>
    /// Unknown or custom shell. Environment variables are skipped to avoid unexpected behavior.
    /// Use this for shells that don't fit other categories or when you want to disable
    /// environment variable application.
    /// </summary>
    Other = 5
}
