using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for launching SSH connections in Windows Terminal.
/// </summary>
public sealed class ExternalTerminalService : IExternalTerminalService
{
    // Strict hostname pattern: DNS labels, IPv4, or bracketed IPv6 literals with optional port.
    // Allows: alphanumeric, hyphens, dots, bracketed IPv6 (e.g. [::1]), colons only inside brackets.
    private static readonly Regex HostnamePattern =
        new(@"^(\[[\da-fA-F:]+\]|[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // POSIX username: starts with letter or underscore, followed by letters, digits, hyphens, underscores, dots.
    // Max 32 chars per POSIX (Linux useradd limit).
    private static readonly Regex UsernamePattern =
        new(@"^[a-zA-Z_][a-zA-Z0-9_\-\.]{0,31}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // cmd.exe metacharacters that must be rejected in file paths used in the cmd.exe fallback.
    // EscapeForCmdExe handles these for simple string values but a path containing them is
    // almost certainly malicious or corrupted, so we reject outright rather than try to escape.
    private static readonly Regex CmdMetacharacterPattern =
        new(@"[&|<>^%!]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILogger<ExternalTerminalService> _logger;
    private readonly Lazy<bool> _isWindowsTerminalAvailable;

    public ExternalTerminalService(ILogger<ExternalTerminalService> logger)
    {
        _logger = logger;
        _isWindowsTerminalAvailable = new Lazy<bool>(CheckWindowsTerminalAvailable);
    }

    public bool IsWindowsTerminalAvailable => _isWindowsTerminalAvailable.Value;

    public async Task<bool> LaunchSshConnectionAsync(HostEntry host, string? password = null)
    {
        if (host.ConnectionType != ConnectionType.Ssh)
        {
            _logger.LogWarning("External terminal only supports SSH connections. ConnectionType: {ConnectionType}",
                host.ConnectionType);
            return false;
        }

        try
        {
            // Validate hostname and username before building any command string.
            if (!IsValidHostname(host.Hostname))
            {
                _logger.LogError(
                    "Refusing to launch external terminal: hostname contains invalid characters: {Hostname}",
                    host.Hostname);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(host.Username) && !IsValidUsername(host.Username))
            {
                _logger.LogError(
                    "Refusing to launch external terminal: username contains invalid characters: {Username}",
                    host.Username);
                return false;
            }

            // Build the ordered list of ssh arguments (no shell involved here).
            var sshArgs = BuildSshArguments(host, password);

            _logger.LogInformation("Launching external terminal for {DisplayName} ({Username}@{Hostname}:{Port})",
                host.DisplayName, host.Username, host.Hostname, host.Port);

            // Try Windows Terminal first, fall back to cmd with ssh.
            if (IsWindowsTerminalAvailable)
            {
                return await LaunchWindowsTerminalAsync(sshArgs, host.DisplayName);
            }
            else
            {
                return await LaunchCmdWithSshAsync(sshArgs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch external terminal for {DisplayName}", host.DisplayName);
            return false;
        }
    }

    /// <summary>
    /// Validates that a hostname contains only safe characters (DNS labels, IPv4, bracketed IPv6).
    /// </summary>
    private static bool IsValidHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return false;

        return HostnamePattern.IsMatch(hostname);
    }

    /// <summary>
    /// Validates that a username conforms to POSIX naming rules.
    /// </summary>
    private static bool IsValidUsername(string username)
    {
        return UsernamePattern.IsMatch(username);
    }

    /// <summary>
    /// Returns the ordered list of arguments to pass to ssh.exe.
    /// No shell quoting is applied here; callers use ArgumentList for injection-safe spawning,
    /// or EscapeForCmdExe when the argument must pass through cmd.exe /K.
    /// </summary>
    private List<string> BuildSshArguments(HostEntry host, string? password)
    {
        var args = new List<string>();

        // Let SSH use default StrictHostKeyChecking behavior (ask).
        // This ensures the user sees and verifies host key fingerprints on first connection,
        // preventing potential MITM attacks. Do NOT use accept-new which bypasses verification.

        // Port (if non-standard)
        if (host.Port != 22)
        {
            args.Add("-p");
            args.Add(host.Port.ToString());
        }

        // Authentication options
        switch (host.AuthType)
        {
            case AuthType.PrivateKeyFile when !string.IsNullOrWhiteSpace(host.PrivateKeyPath):
                var keyPath = Environment.ExpandEnvironmentVariables(host.PrivateKeyPath);
                if (CmdMetacharacterPattern.IsMatch(keyPath))
                {
                    throw new ArgumentException(
                        $"PrivateKeyPath contains cmd.exe metacharacters and cannot be used safely: {keyPath}");
                }
                args.Add("-i");
                args.Add(keyPath);
                break;

            case AuthType.Password:
                // Note: OpenSSH doesn't support password on command line for security reasons.
                // The user will be prompted interactively.
                _logger.LogDebug("Password authentication requested - user will be prompted interactively");
                break;

            case AuthType.SshAgent:
                // SSH agent is used by default, no extra options needed.
                break;
        }

        // User@Host  (already validated above; kept as separate argument, not interpolated into a shell)
        if (!string.IsNullOrWhiteSpace(host.Username))
        {
            args.Add($"{host.Username}@{host.Hostname}");
        }
        else
        {
            args.Add(host.Hostname);
        }

        return args;
    }

    private async Task<bool> LaunchWindowsTerminalAsync(List<string> sshArgs, string tabTitle)
    {
        try
        {
            // Windows Terminal supports ArgumentList; build the argument list directly.
            // wt.exe --title "<title>" ssh [ssh-args...]
            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(tabTitle);
            startInfo.ArgumentList.Add("ssh");

            foreach (var arg in sshArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            _logger.LogDebug("Launching Windows Terminal: wt.exe --title {Title} ssh {Args}",
                tabTitle, string.Join(" ", sshArgs));

            using var process = Process.Start(startInfo);

            if (process == null)
            {
                _logger.LogError("Failed to start Windows Terminal process");
                return false;
            }

            // Give it a moment to start
            await Task.Delay(100);

            _logger.LogInformation("Windows Terminal launched successfully for SSH connection");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Windows Terminal");
            return false;
        }
    }

    private async Task<bool> LaunchCmdWithSshAsync(List<string> sshArgs)
    {
        try
        {
            // Fallback: launch cmd.exe /K ssh [args...]
            // /K keeps the window open after SSH exits.
            // We must pass everything as a single Arguments string to cmd.exe because
            // cmd.exe interprets its command line itself; use EscapeForCmdExe to neutralise
            // all cmd.exe metacharacters before embedding.
            var sshCommandParts = new StringBuilder("ssh");
            foreach (var arg in sshArgs)
            {
                sshCommandParts.Append(' ');
                sshCommandParts.Append(EscapeForCmdExe(arg));
            }

            var cmdArguments = "/K " + sshCommandParts;

            _logger.LogDebug("Launching cmd.exe with: {Arguments}", cmdArguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArguments,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);

            if (process == null)
            {
                _logger.LogError("Failed to start cmd.exe process");
                return false;
            }

            await Task.Delay(100);

            _logger.LogInformation("cmd.exe launched successfully for SSH connection");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch cmd.exe with SSH");
            return false;
        }
    }

    private static bool CheckWindowsTerminalAvailable()
    {
        try
        {
            // Check if wt.exe is in PATH or Windows Apps folder
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "wt.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(3000);

            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Escapes a single token so it can be embedded inside a cmd.exe command string without
    /// being interpreted as a shell metacharacter.  The strategy is:
    /// 1. Double every existing caret (^) so existing escapes are preserved.
    /// 2. Escape all other cmd.exe metacharacters with a leading caret.
    /// 3. Wrap the result in double-quotes if it contains spaces, so the token is treated
    ///    as a single argument by the child process.
    /// </summary>
    private static string EscapeForCmdExe(string value)
    {
        // Step 1: escape existing carets first (order matters).
        var result = value.Replace("^", "^^");

        // Step 2: escape all other cmd.exe special characters.
        // Characters that cmd.exe interprets: & | > < % ! ( ) " @
        // Note: double-quote is handled by wrapping below, but we still escape it here
        // in case the value is used in a context without wrapping.
        result = result
            .Replace("&", "^&")
            .Replace("|", "^|")
            .Replace(">", "^>")
            .Replace("<", "^<")
            .Replace("%", "^%")
            .Replace("!", "^!")
            .Replace("(", "^(")
            .Replace(")", "^)")
            .Replace("@", "^@");

        // Step 3: if the value contains spaces (or is empty), wrap in double-quotes so the
        // child process receives it as a single argument.  Embedded double-quotes inside
        // the value are already neutralised by the ^& treatment above; we additionally
        // double them per the Win32 argv-parsing convention so the child sees the literal ".
        if (result.Contains(' ') || result.Length == 0)
        {
            result = "\"" + result.Replace("\"", "\"\"") + "\"";
        }

        return result;
    }
}
