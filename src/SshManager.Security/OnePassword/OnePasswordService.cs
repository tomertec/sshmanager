using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Security.OnePassword;

/// <summary>
/// Wraps the 1Password CLI (op) to fetch secrets and list vault items.
/// Requires the 1Password desktop app with CLI integration enabled for biometric auth.
/// </summary>
public sealed class OnePasswordService : IOnePasswordService
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<OnePasswordService> _logger;

    public OnePasswordService(ILogger<OnePasswordService>? logger = null)
    {
        _logger = logger ?? NullLogger<OnePasswordService>.Instance;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunOpAsync("--version", StatusTimeout, ct);
        return exitCode == 0;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunOpAsync("whoami --format json", StatusTimeout, ct);
        return exitCode == 0;
    }

    public async Task<OnePasswordStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var isInstalled = await IsInstalledAsync(ct);
        if (!isInstalled)
        {
            return new OnePasswordStatus(false, false, null, null);
        }

        var (exitCode, stdout, stderr) = await RunOpAsync("whoami --format json", StatusTimeout, ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            // Try 'op signin' to trigger desktop app biometric unlock
            _logger.LogDebug("op whoami failed, attempting op signin to trigger desktop app unlock");
            var (signInExit, _, _) = await RunOpAsync("signin", DataTimeout, ct);
            if (signInExit == 0)
            {
                // Retry whoami after signin
                (exitCode, stdout, stderr) = await RunOpAsync("whoami --format json", StatusTimeout, ct);
            }
        }

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            var errorMsg = string.IsNullOrWhiteSpace(stderr) ? null : stderr.Trim();
            _logger.LogDebug("op whoami failed: {Error}", errorMsg);
            return new OnePasswordStatus(true, false, null, null, errorMsg);
        }

        try
        {
            var whoami = JsonSerializer.Deserialize<OpWhoamiResponse>(stdout, JsonOptions);
            return new OnePasswordStatus(
                true,
                true,
                whoami?.Email,
                whoami?.Url);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse 'op whoami' JSON output");
            return new OnePasswordStatus(true, true, null, null);
        }
    }

    public async Task<string?> ReadSecretAsync(string secretReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
            return null;

        if (!secretReference.StartsWith("op://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Invalid secret reference format: must start with 'op://'");
            return null;
        }

        var escapedRef = EscapeArgument(secretReference);
        var (exitCode, stdout, stderr) = await RunOpAsync($"read {escapedRef}", DataTimeout, ct);

        if (exitCode != 0)
        {
            _logger.LogWarning("Failed to read secret from 1Password: {Error}", stderr);
            return null;
        }

        return stdout?.TrimEnd('\r', '\n');
    }

    public async Task<string?> ReadSshKeyAsync(string secretReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
            return null;

        if (!secretReference.StartsWith("op://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Invalid SSH key reference format: must start with 'op://'");
            return null;
        }

        // Append ssh-format query parameter if not already present
        var reference = secretReference;
        if (!reference.Contains("ssh-format=", StringComparison.OrdinalIgnoreCase))
        {
            reference += reference.Contains('?') ? "&ssh-format=openssh" : "?ssh-format=openssh";
        }

        var escapedRef = EscapeArgument(reference);
        var (exitCode, stdout, stderr) = await RunOpAsync($"read {escapedRef}", DataTimeout, ct);

        if (exitCode != 0)
        {
            _logger.LogWarning("Failed to read SSH key from 1Password: {Error}", stderr);
            return null;
        }

        return stdout?.TrimEnd('\r', '\n');
    }

    public async Task<IReadOnlyList<OnePasswordVault>> ListVaultsAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, stderr) = await RunOpAsync("vault list --format json --no-color", DataTimeout, ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogWarning("Failed to list 1Password vaults: {Error}", stderr);
            return Array.Empty<OnePasswordVault>();
        }

        try
        {
            var vaults = JsonSerializer.Deserialize<List<OpVaultResponse>>(stdout, JsonOptions);
            return vaults?.Select(v => new OnePasswordVault(v.Id ?? "", v.Name ?? "")).ToList()
                   ?? (IReadOnlyList<OnePasswordVault>)Array.Empty<OnePasswordVault>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse vault list JSON");
            return Array.Empty<OnePasswordVault>();
        }
    }

    public async Task<IReadOnlyList<OnePasswordItem>> ListItemsAsync(
        string? vaultId = null,
        string? query = null,
        CancellationToken ct = default)
    {
        var args = "item list --format json --no-color --categories \"Login,SSH Key,Password,Server\"";

        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            args += $" --vault {EscapeArgument(vaultId)}";
        }

        // 1Password CLI uses --query for searching (not --search)
        if (!string.IsNullOrWhiteSpace(query))
        {
            // op item list doesn't have a --query flag; filter client-side instead
        }

        var (exitCode, stdout, stderr) = await RunOpAsync(args, DataTimeout, ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogWarning("Failed to list 1Password items: {Error}", stderr);
            return Array.Empty<OnePasswordItem>();
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<OpItemResponse>>(stdout, JsonOptions);
            if (items == null) return Array.Empty<OnePasswordItem>();

            var result = items.Select(i => new OnePasswordItem(
                i.Id ?? "",
                i.Title ?? "",
                i.Category ?? "",
                new OnePasswordVault(i.Vault?.Id ?? "", i.Vault?.Name ?? ""),
                i.Tags,
                i.Urls?.FirstOrDefault(u => u.Primary)?.Href ?? i.Urls?.FirstOrDefault()?.Href)).ToList();

            // Client-side filter if query provided
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                result = result.Where(i =>
                    i.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse item list JSON");
            return Array.Empty<OnePasswordItem>();
        }
    }

    public async Task<OnePasswordItemDetail?> GetItemAsync(
        string itemId,
        string? vaultId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        var args = $"item get {EscapeArgument(itemId)} --format json --no-color --reveal";

        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            args += $" --vault {EscapeArgument(vaultId)}";
        }

        var (exitCode, stdout, stderr) = await RunOpAsync(args, DataTimeout, ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogWarning("Failed to get 1Password item {ItemId}: {Error}", itemId, stderr);
            return null;
        }

        try
        {
            var item = JsonSerializer.Deserialize<OpItemDetailResponse>(stdout, JsonOptions);
            if (item == null) return null;

            var fields = (item.Fields ?? Array.Empty<OpFieldResponse>())
                .Select(f => new OnePasswordField(
                    f.Id ?? "",
                    f.Label ?? "",
                    f.Type ?? "",
                    f.Value,
                    f.Reference,
                    f.Section?.Label))
                .ToList();

            return new OnePasswordItemDetail(
                item.Id ?? "",
                item.Title ?? "",
                item.Category ?? "",
                new OnePasswordVault(item.Vault?.Id ?? "", item.Vault?.Name ?? ""),
                fields);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse item detail JSON for {ItemId}", itemId);
            return null;
        }
    }

    #region Private Helpers

    /// <summary>
    /// Runs the op CLI with the given arguments and returns (exitCode, stdout, stderr).
    /// </summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunOpAsync(
        string arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        _logger.LogDebug("Running: op {Arguments}", arguments);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var psi = new ProcessStartInfo
            {
                FileName = "op",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Ensure 1Password desktop app integration works
                Environment =
                {
                    ["OP_INTEGRATION_NAME"] = "SshManager",
                    ["OP_BIOMETRIC_UNLOCK_ENABLED"] = "true"
                }
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            // Read output and error concurrently
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            _logger.LogDebug("op exited with code {ExitCode}", process.ExitCode);

            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("1Password CLI command timed out after {Timeout}s: op {Arguments}",
                timeout.TotalSeconds, arguments);
            return (-1, "", "Operation timed out");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            _logger.LogDebug("1Password CLI (op) not found: {Message}", ex.Message);
            return (-1, "", "1Password CLI not found. Install from https://1password.com/downloads/command-line/");
        }
    }

    /// <summary>
    /// Escapes an argument for safe command-line usage.
    /// </summary>
    private static string EscapeArgument(string arg)
    {
        // Wrap in quotes if it contains spaces or special characters
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('&') || arg.Contains('|'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        return arg;
    }

    #endregion

    #region JSON DTOs for op CLI output

    private sealed class OpWhoamiResponse
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class OpVaultResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class OpItemResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("category")]
        public string? Category { get; set; }
        [JsonPropertyName("vault")]
        public OpVaultRef? Vault { get; set; }
        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }
        [JsonPropertyName("urls")]
        public OpUrlRef[]? Urls { get; set; }
    }

    private sealed class OpUrlRef
    {
        [JsonPropertyName("href")]
        public string? Href { get; set; }
        [JsonPropertyName("primary")]
        public bool Primary { get; set; }
    }

    private sealed class OpItemDetailResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("category")]
        public string? Category { get; set; }
        [JsonPropertyName("vault")]
        public OpVaultRef? Vault { get; set; }
        [JsonPropertyName("fields")]
        public OpFieldResponse[]? Fields { get; set; }
    }

    private sealed class OpVaultRef
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class OpFieldResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("label")]
        public string? Label { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("value")]
        public string? Value { get; set; }
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }
        [JsonPropertyName("section")]
        public OpSectionRef? Section { get; set; }
    }

    private sealed class OpSectionRef
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    #endregion
}
