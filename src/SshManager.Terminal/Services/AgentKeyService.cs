using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshNet.Agent;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for managing SSH keys in SSH agents (Pageant and Windows OpenSSH Agent).
/// Uses ssh-add command for OpenSSH Agent and Pageant's Windows API for Pageant.
/// </summary>
public sealed partial class AgentKeyService : IAgentKeyService
{
    private readonly ILogger<AgentKeyService> _logger;
    private readonly IAgentDiagnosticsService _diagnostics;

    public AgentKeyService(
        IAgentDiagnosticsService diagnostics,
        ILogger<AgentKeyService>? logger = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? NullLogger<AgentKeyService>.Instance;
    }

    /// <inheritdoc />
    public async Task<AgentKeyOperationResult> AddKeyToAgentAsync(
        string privateKeyPath,
        string? passphrase = null,
        TimeSpan? lifetime = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Adding key to agent: {KeyPath}", privateKeyPath);

        if (!File.Exists(privateKeyPath))
        {
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: null,
                Fingerprint: null,
                ErrorMessage: $"Private key file not found: {privateKeyPath}");
        }

        var availability = await GetAgentAvailabilityAsync(ct);

        if (availability.PreferredAgent == null)
        {
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: null,
                Fingerprint: null,
                ErrorMessage: "No SSH agent is available. Please start OpenSSH Agent service or run Pageant.");
        }

        if (availability.PreferredAgent == "OpenSSH Agent")
        {
            return await AddKeyToOpenSshAgentAsync(privateKeyPath, passphrase, lifetime, ct);
        }
        else // Pageant
        {
            return await AddKeyToPageantAsync(privateKeyPath, passphrase, ct);
        }
    }

    /// <inheritdoc />
    public async Task<AgentKeyOperationResult> AddKeyContentToAgentAsync(
        string privateKeyContent,
        string? passphrase = null,
        TimeSpan? lifetime = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Adding key from memory content to agent");

        var availability = await GetAgentAvailabilityAsync(ct);

        if (availability.PreferredAgent == null)
        {
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: null,
                Fingerprint: null,
                ErrorMessage: "No SSH agent is available. Please start OpenSSH Agent service or run Pageant.");
        }

        // Create a temporary file with the key content
        // This is necessary because both ssh-add and Pageant require file paths
        var tempKeyPath = await CreateSecureTempKeyFileAsync(privateKeyContent, ct);

        try
        {
            if (availability.PreferredAgent == "OpenSSH Agent")
            {
                return await AddKeyToOpenSshAgentAsync(tempKeyPath, passphrase, lifetime, ct);
            }
            else // Pageant
            {
                return await AddKeyToPageantAsync(tempKeyPath, passphrase, ct);
            }
        }
        finally
        {
            await SecureDeleteFileAsync(tempKeyPath);
        }
    }

    /// <inheritdoc />
    public async Task<AgentKeyOperationResult> RemoveKeyFromAgentAsync(
        string publicKeyPathOrFingerprint,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Removing key from agent: {KeyIdentifier}", publicKeyPathOrFingerprint);

        var availability = await GetAgentAvailabilityAsync(ct);

        if (availability.PreferredAgent == null)
        {
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: null,
                Fingerprint: null,
                ErrorMessage: "No SSH agent is available.");
        }

        if (availability.PreferredAgent == "OpenSSH Agent")
        {
            return await RemoveKeyFromOpenSshAgentAsync(publicKeyPathOrFingerprint, ct);
        }
        else // Pageant
        {
            _logger.LogWarning("Pageant does not support programmatic key removal. Please use Pageant's GUI.");
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: "Pageant",
                Fingerprint: null,
                ErrorMessage: "Pageant does not support programmatic key removal. Please use Pageant's GUI to remove keys.");
        }
    }

    /// <inheritdoc />
    public async Task<AgentKeyOperationResult> RemoveAllKeysAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Removing all keys from agent");

        var availability = await GetAgentAvailabilityAsync(ct);

        if (availability.PreferredAgent == null)
        {
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: null,
                Fingerprint: null,
                ErrorMessage: "No SSH agent is available.");
        }

        if (availability.PreferredAgent == "OpenSSH Agent")
        {
            return await RemoveAllKeysFromOpenSshAgentAsync(ct);
        }
        else // Pageant
        {
            _logger.LogWarning("Pageant does not support programmatic key removal. Please use Pageant's GUI.");
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: "Pageant",
                Fingerprint: null,
                ErrorMessage: "Pageant does not support programmatic key removal. Please use Pageant's GUI to remove keys.");
        }
    }

    /// <inheritdoc />
    public async Task<AgentAvailability> GetAgentAvailabilityAsync(CancellationToken ct = default)
    {
        // Refresh diagnostics to get latest agent state
        await _diagnostics.RefreshAsync(ct);

        bool pageantAvailable = _diagnostics.IsPageantAvailable;
        bool openSshAvailable = await IsOpenSshAgentServiceRunningAsync(ct);

        // Prefer Pageant if it's available (matches SshAuthenticationFactory logic)
        string? preferred = pageantAvailable ? "Pageant" :
                           openSshAvailable ? "OpenSSH Agent" :
                           null;

        _logger.LogDebug(
            "Agent availability: Pageant={Pageant}, OpenSSH={OpenSSH}, Preferred={Preferred}",
            pageantAvailable, openSshAvailable, preferred ?? "None");

        return new AgentAvailability(pageantAvailable, openSshAvailable, preferred);
    }

    #region OpenSSH Agent Operations

    /// <summary>
    /// Adds a key to the OpenSSH Agent using ssh-add command.
    /// </summary>
    private async Task<AgentKeyOperationResult> AddKeyToOpenSshAgentAsync(
        string privateKeyPath,
        string? passphrase,
        TimeSpan? lifetime,
        CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ssh-add",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Add lifetime argument if specified
            if (lifetime.HasValue)
            {
                var seconds = (int)lifetime.Value.TotalSeconds;
                if (seconds > 0)
                {
                    startInfo.ArgumentList.Add("-t");
                    startInfo.ArgumentList.Add(seconds.ToString());
                }
            }

            // Add the key path
            startInfo.ArgumentList.Add(privateKeyPath);

            var output = new StringBuilder();
            var error = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    error.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // If passphrase is provided, send it to stdin
            if (!string.IsNullOrEmpty(passphrase))
            {
                await process.StandardInput.WriteLineAsync(passphrase);
            }

            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            await process.WaitForExitAsync(ct);

            var outputStr = output.ToString().Trim();
            var errorStr = error.ToString().Trim();

            if (process.ExitCode == 0)
            {
                // Get the fingerprint of the added key
                var fingerprint = await GetKeyFingerprintAsync(privateKeyPath, ct);

                _logger.LogInformation(
                    "Successfully added key to OpenSSH Agent: {Fingerprint}",
                    fingerprint ?? "unknown");

                return new AgentKeyOperationResult(
                    Success: true,
                    AgentType: "OpenSSH Agent",
                    Fingerprint: fingerprint,
                    ErrorMessage: null);
            }
            else
            {
                var errorMessage = string.IsNullOrEmpty(errorStr) ? outputStr : errorStr;
                _logger.LogWarning("Failed to add key to OpenSSH Agent: {Error}", errorMessage);

                return new AgentKeyOperationResult(
                    Success: false,
                    AgentType: "OpenSSH Agent",
                    Fingerprint: null,
                    ErrorMessage: $"ssh-add failed: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding key to OpenSSH Agent");
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: "OpenSSH Agent",
                Fingerprint: null,
                ErrorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a key from the OpenSSH Agent using ssh-add -d command.
    /// </summary>
    private async Task<AgentKeyOperationResult> RemoveKeyFromOpenSshAgentAsync(
        string publicKeyPathOrFingerprint,
        CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ssh-add",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(publicKeyPathOrFingerprint);

            var output = await RunProcessAsync(startInfo, ct);

            if (output.ExitCode == 0)
            {
                _logger.LogInformation("Successfully removed key from OpenSSH Agent");
                return new AgentKeyOperationResult(
                    Success: true,
                    AgentType: "OpenSSH Agent",
                    Fingerprint: null,
                    ErrorMessage: null);
            }
            else
            {
                var errorMessage = string.IsNullOrEmpty(output.Error) ? output.Output : output.Error;
                _logger.LogWarning("Failed to remove key from OpenSSH Agent: {Error}", errorMessage);

                return new AgentKeyOperationResult(
                    Success: false,
                    AgentType: "OpenSSH Agent",
                    Fingerprint: null,
                    ErrorMessage: $"ssh-add -d failed: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing key from OpenSSH Agent");
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: "OpenSSH Agent",
                Fingerprint: null,
                ErrorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes all keys from the OpenSSH Agent using ssh-add -D command.
    /// </summary>
    private async Task<AgentKeyOperationResult> RemoveAllKeysFromOpenSshAgentAsync(CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ssh-add",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-D");

            var output = await RunProcessAsync(startInfo, ct);

            if (output.ExitCode == 0)
            {
                _logger.LogInformation("Successfully removed all keys from OpenSSH Agent");
                return new AgentKeyOperationResult(
                    Success: true,
                    AgentType: "OpenSSH Agent",
                    Fingerprint: null,
                    ErrorMessage: null);
            }
            else
            {
                var errorMessage = string.IsNullOrEmpty(output.Error) ? output.Output : output.Error;
                _logger.LogWarning("Failed to remove all keys from OpenSSH Agent: {Error}", errorMessage);

                return new AgentKeyOperationResult(
                    Success: false,
                    AgentType: "OpenSSH Agent",
                    Fingerprint: null,
                    ErrorMessage: $"ssh-add -D failed: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing all keys from OpenSSH Agent");
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: "OpenSSH Agent",
                Fingerprint: null,
                ErrorMessage: $"Error: {ex.Message}");
        }
    }

    #endregion

    #region Pageant Operations

    /// <summary>
    /// Adds a key to Pageant by launching pageant.exe with the key file.
    /// Note: This assumes pageant.exe is in PATH or installed in a standard location.
    /// </summary>
    private async Task<AgentKeyOperationResult> AddKeyToPageantAsync(
        string privateKeyPath,
        string? passphrase,
        CancellationToken ct)
    {
        try
        {
            // Pageant loads keys via command line: pageant.exe keyfile.ppk
            // For OpenSSH format keys, we need to check if Pageant supports them
            // Modern versions of Pageant (from PuTTY 0.75+) support OpenSSH format

            var pageantPath = FindPageantExecutable();
            if (pageantPath == null)
            {
                _logger.LogWarning("Pageant executable not found in PATH or standard locations");
                return new AgentKeyOperationResult(
                    Success: false,
                    AgentType: "Pageant",
                    Fingerprint: null,
                    ErrorMessage: "Pageant executable (pageant.exe) not found. Please ensure PuTTY is installed and in PATH.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pageantPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Pageant command: pageant.exe --add keyfile
            startInfo.ArgumentList.Add("--add");
            startInfo.ArgumentList.Add(privateKeyPath);

            var output = await RunProcessAsync(startInfo, ct);

            // Pageant typically exits immediately after adding the key
            // If the key requires a passphrase and we don't provide it interactively,
            // Pageant will show a GUI dialog (can't be automated easily)

            if (output.ExitCode == 0)
            {
                var fingerprint = await GetKeyFingerprintAsync(privateKeyPath, ct);

                _logger.LogInformation(
                    "Successfully added key to Pageant: {Fingerprint}",
                    fingerprint ?? "unknown");

                return new AgentKeyOperationResult(
                    Success: true,
                    AgentType: "Pageant",
                    Fingerprint: fingerprint,
                    ErrorMessage: null);
            }
            else
            {
                var errorMessage = string.IsNullOrEmpty(output.Error) ? output.Output : output.Error;

                // If key is encrypted and no passphrase provided, Pageant shows GUI
                if (!string.IsNullOrEmpty(passphrase))
                {
                    errorMessage = "Pageant GUI may be showing a passphrase prompt. Note: Automated passphrase entry for Pageant is not supported.";
                }

                _logger.LogWarning("Pageant returned non-zero exit code: {Error}", errorMessage);

                return new AgentKeyOperationResult(
                    Success: false,
                    AgentType: "Pageant",
                    Fingerprint: null,
                    ErrorMessage: errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding key to Pageant");
            return new AgentKeyOperationResult(
                Success: false,
                AgentType: "Pageant",
                Fingerprint: null,
                ErrorMessage: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to find the Pageant executable in common locations.
    /// </summary>
    private string? FindPageantExecutable()
    {
        // Check if pageant.exe is in PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();

        foreach (var dir in pathDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;

            var pageantPath = Path.Combine(dir, "pageant.exe");
            if (File.Exists(pageantPath))
            {
                _logger.LogDebug("Found Pageant in PATH: {Path}", pageantPath);
                return pageantPath;
            }
        }

        // Check standard PuTTY installation locations
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", "pageant.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PuTTY", "pageant.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PuTTY", "pageant.exe")
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found Pageant at: {Path}", path);
                return path;
            }
        }

        _logger.LogDebug("Pageant executable not found");
        return null;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if the OpenSSH Agent service is running on Windows.
    /// </summary>
    private async Task<bool> IsOpenSshAgentServiceRunningAsync(CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc",
                ArgumentList = { "query", "ssh-agent" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var output = await RunProcessAsync(startInfo, ct);

            // Check if output contains "RUNNING"
            bool isRunning = output.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug("OpenSSH Agent service running: {IsRunning}", isRunning);

            return isRunning;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking OpenSSH Agent service status");
            return false;
        }
    }

    /// <summary>
    /// Gets the fingerprint of a private key by reading its corresponding public key.
    /// </summary>
    private async Task<string?> GetKeyFingerprintAsync(string privateKeyPath, CancellationToken ct)
    {
        try
        {
            // Try to use ssh-keygen to get fingerprint
            var startInfo = new ProcessStartInfo
            {
                FileName = "ssh-keygen",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-lf");
            startInfo.ArgumentList.Add(privateKeyPath);

            var output = await RunProcessAsync(startInfo, ct);

            if (output.ExitCode == 0 && !string.IsNullOrEmpty(output.Output))
            {
                // Parse fingerprint from ssh-keygen output
                // Format: "2048 SHA256:xxxxx... comment (RSA)"
                var match = FingerprintRegex().Match(output.Output);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting key fingerprint with ssh-keygen");
        }

        return null;
    }

    /// <summary>
    /// Creates a temporary file with secure permissions for storing a private key.
    /// </summary>
    private async Task<string> CreateSecureTempKeyFileAsync(string keyContent, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SshManager", "TempKeys");
        Directory.CreateDirectory(tempDir);

        var tempFile = Path.Combine(tempDir, $"key_{Guid.NewGuid():N}");

        // Write the key content
        await File.WriteAllTextAsync(tempFile, keyContent, ct);

        // Set restrictive permissions (Windows ACL - only current user)
        try
        {
            var fileInfo = new FileInfo(tempFile);
            var security = fileInfo.GetAccessControl();

            // Disable inheritance
            security.SetAccessRuleProtection(true, false);

            // Remove all existing rules
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount)))
            {
                security.RemoveAccessRule(rule);
            }

            // Add rule for current user only
            var currentUser = WindowsIdentity.GetCurrent().Name;
            var accessRule = new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow);

            security.AddAccessRule(accessRule);
            fileInfo.SetAccessControl(security);

            _logger.LogDebug("Created secure temp key file: {Path}", tempFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set secure permissions on temp key file");
        }

        return tempFile;
    }

    /// <summary>
    /// Securely deletes a file by overwriting it with random data before deletion.
    /// </summary>
    private async Task SecureDeleteFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            // Overwrite file with random data
            var fileInfo = new FileInfo(filePath);
            var fileLength = fileInfo.Length;

            if (fileLength > 0)
            {
                var randomData = new byte[fileLength];
                RandomNumberGenerator.Fill(randomData);

                await File.WriteAllBytesAsync(filePath, randomData);
            }

            // Delete the file
            File.Delete(filePath);

            _logger.LogDebug("Securely deleted temp key file: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error securely deleting temp file: {Path}", filePath);
        }
    }

    /// <summary>
    /// Runs a process and captures its output.
    /// </summary>
    private static async Task<ProcessOutput> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var process = new Process { StartInfo = startInfo };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        return new ProcessOutput(
            process.ExitCode,
            output.ToString().Trim(),
            error.ToString().Trim());
    }

    /// <summary>
    /// Regex for extracting fingerprint from ssh-keygen output.
    /// </summary>
    [GeneratedRegex(@"(SHA256:[A-Za-z0-9+/]+)")]
    private static partial Regex FingerprintRegex();

    private record ProcessOutput(int ExitCode, string Output, string Error);

    #endregion
}
