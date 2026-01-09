using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for launching SSH connections in Windows Terminal.
/// </summary>
public sealed class ExternalTerminalService : IExternalTerminalService
{
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
            var sshCommand = BuildSshCommand(host, password);

            _logger.LogInformation("Launching external terminal for {DisplayName} ({Username}@{Hostname}:{Port})",
                host.DisplayName, host.Username, host.Hostname, host.Port);

            // Try Windows Terminal first, fall back to cmd with ssh
            if (IsWindowsTerminalAvailable)
            {
                return await LaunchWindowsTerminalAsync(sshCommand, host.DisplayName);
            }
            else
            {
                return await LaunchCmdWithSshAsync(sshCommand);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch external terminal for {DisplayName}", host.DisplayName);
            return false;
        }
    }

    private string BuildSshCommand(HostEntry host, string? password)
    {
        var sb = new StringBuilder("ssh");

        // Let SSH use default StrictHostKeyChecking behavior (ask).
        // This ensures the user sees and verifies host key fingerprints on first connection,
        // preventing potential MITM attacks. Do NOT use accept-new which bypasses verification.

        // Port (if non-standard)
        if (host.Port != 22)
        {
            sb.Append($" -p {host.Port}");
        }

        // Authentication options
        switch (host.AuthType)
        {
            case AuthType.PrivateKeyFile when !string.IsNullOrWhiteSpace(host.PrivateKeyPath):
                var keyPath = Environment.ExpandEnvironmentVariables(host.PrivateKeyPath);
                // Quote the path in case it contains spaces
                sb.Append($" -i \"{keyPath}\"");
                break;

            case AuthType.Password:
                // Note: OpenSSH doesn't support password on command line for security reasons.
                // The user will be prompted interactively.
                _logger.LogDebug("Password authentication requested - user will be prompted interactively");
                break;

            case AuthType.SshAgent:
                // SSH agent is used by default, no extra options needed
                break;
        }

        // User@Host
        if (!string.IsNullOrWhiteSpace(host.Username))
        {
            sb.Append($" {host.Username}@{host.Hostname}");
        }
        else
        {
            sb.Append($" {host.Hostname}");
        }

        return sb.ToString();
    }

    private async Task<bool> LaunchWindowsTerminalAsync(string sshCommand, string tabTitle)
    {
        try
        {
            // Windows Terminal command: wt.exe --title "Tab Title" ssh ...
            var arguments = $"--title \"{EscapeForCommandLine(tabTitle)}\" {sshCommand}";

            _logger.LogDebug("Launching Windows Terminal with: wt.exe {Arguments}", arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = arguments,
                UseShellExecute = true
            };

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

    private async Task<bool> LaunchCmdWithSshAsync(string sshCommand)
    {
        try
        {
            // Fallback: Launch cmd.exe with the SSH command
            // The /K flag keeps the window open after SSH exits
            var arguments = $"/K {sshCommand}";

            _logger.LogDebug("Launching cmd.exe with: {Arguments}", arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
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

    private static string EscapeForCommandLine(string value)
    {
        // Escape double quotes and backslashes for command line
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
