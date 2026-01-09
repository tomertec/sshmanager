using SshManager.Security;
using Microsoft.Extensions.Logging;

namespace SshManager.Examples;

/// <summary>
/// Demonstrates how to use the batch PPK conversion functionality.
/// </summary>
public class BatchPpkConversionExample
{
    private readonly IPpkConverter _ppkConverter;
    private readonly ILogger<BatchPpkConversionExample> _logger;

    public BatchPpkConversionExample(IPpkConverter ppkConverter, ILogger<BatchPpkConversionExample> logger)
    {
        _ppkConverter = ppkConverter;
        _logger = logger;
    }

    /// <summary>
    /// Example 1: Convert multiple PPK files without saving (useful for validation or importing into memory).
    /// </summary>
    public async Task ConvertMultiplePpkFilesAsync(CancellationToken ct = default)
    {
        // Define files to convert with their passphrases
        var ppkFiles = new[]
        {
            (@"C:\keys\server1.ppk", (string?)null),           // Unencrypted key
            (@"C:\keys\server2.ppk", "mypassphrase123"),       // Encrypted with passphrase
            (@"C:\keys\database.ppk", "db_password"),          // Another encrypted key
            (@"C:\keys\backup.ppk", (string?)null)             // Another unencrypted key
        };

        // Convert all files in batch
        var result = await _ppkConverter.ConvertBatchToOpenSshAsync(ppkFiles, ct);

        // Display results
        _logger.LogInformation("Batch Conversion Summary:");
        _logger.LogInformation("Total: {Total}, Success: {Success}, Failed: {Failed}",
            result.TotalCount, result.SuccessCount, result.FailureCount);

        // Process individual results
        foreach (var item in result.Results)
        {
            if (item.Success)
            {
                _logger.LogInformation("Converted: {Path}", item.PpkPath);
                _logger.LogInformation("  Key Type: {KeyType}", item.ConversionResult?.KeyType);
                _logger.LogInformation("  Fingerprint: {Fingerprint}", item.ConversionResult?.Fingerprint);

                // The OpenSSH keys are available in item.ConversionResult.OpenSshPrivateKey
                // and item.ConversionResult.OpenSshPublicKey
            }
            else
            {
                _logger.LogWarning("Failed: {Path} - {Error}", item.PpkPath, item.ErrorMessage);
            }
        }
    }

    /// <summary>
    /// Example 2: Convert and save multiple PPK files to a directory.
    /// </summary>
    public async Task ConvertAndSaveMultiplePpkFilesAsync(CancellationToken ct = default)
    {
        var outputDirectory = @"C:\keys\converted";

        // Define files to convert
        var ppkFiles = new[]
        {
            (@"C:\keys\production-web.ppk", "prod_password"),
            (@"C:\keys\staging-web.ppk", (string?)null),
            (@"C:\keys\dev-server.ppk", (string?)null)
        };

        // Convert and save all files
        var result = await _ppkConverter.ConvertBatchAndSaveAsync(ppkFiles, outputDirectory, ct);

        // Display results
        _logger.LogInformation("Batch Conversion and Save Summary:");
        _logger.LogInformation("Total: {Total}, Success: {Success}, Failed: {Failed}",
            result.TotalCount, result.SuccessCount, result.FailureCount);

        // Show saved file paths
        foreach (var item in result.Results.Where(r => r.Success))
        {
            _logger.LogInformation("Saved: {Source} -> {Destination}", item.PpkPath, item.SavedPath);
            _logger.LogInformation("  Private Key: {Path}", item.SavedPath);
            _logger.LogInformation("  Public Key: {Path}.pub", item.SavedPath);
        }

        // Show failures
        foreach (var item in result.Results.Where(r => !r.Success))
        {
            _logger.LogWarning("Failed: {Path} - {Error}", item.PpkPath, item.ErrorMessage);
        }
    }

    /// <summary>
    /// Example 3: Process a directory of PPK files with mixed encryption states.
    /// </summary>
    public async Task ConvertDirectoryOfPpkFilesAsync(
        string directoryPath,
        string outputDirectory,
        Dictionary<string, string> passphrasesByFile,
        CancellationToken ct = default)
    {
        // Find all PPK files in directory
        var ppkFilePaths = Directory.GetFiles(directoryPath, "*.ppk", SearchOption.TopDirectoryOnly);

        _logger.LogInformation("Found {Count} PPK files in {Directory}", ppkFilePaths.Length, directoryPath);

        // Build the input list with passphrases
        var ppkFiles = ppkFilePaths.Select(path =>
        {
            var fileName = Path.GetFileName(path);
            var passphrase = passphrasesByFile.GetValueOrDefault(fileName);
            return (path, passphrase);
        });

        // Convert all files
        var result = await _ppkConverter.ConvertBatchAndSaveAsync(ppkFiles, outputDirectory, ct);

        // Summary
        _logger.LogInformation("Conversion Summary:");
        _logger.LogInformation("  Total Files: {Total}", result.TotalCount);
        _logger.LogInformation("  Successful: {Success}", result.SuccessCount);
        _logger.LogInformation("  Failed: {Failed}", result.FailureCount);

        // Details for each result
        foreach (var item in result.Results)
        {
            if (item.Success)
            {
                _logger.LogInformation("[SUCCESS] {FileName}",
                    Path.GetFileName(item.PpkPath));
                _logger.LogInformation("  Converted to: {Path}", item.SavedPath);
            }
            else
            {
                _logger.LogError("[FAILED] {FileName}: {Error}",
                    Path.GetFileName(item.PpkPath), item.ErrorMessage);
            }
        }

        return result;
    }

    /// <summary>
    /// Example 4: Validate multiple PPK files before conversion (check if passphrases are needed).
    /// </summary>
    public async Task ValidatePpkFilesBeforeConversionAsync(string[] ppkFilePaths, CancellationToken ct = default)
    {
        _logger.LogInformation("Validating {Count} PPK files", ppkFilePaths.Length);

        var needsPassphrase = new List<string>();
        var unencrypted = new List<string>();
        var invalid = new List<string>();

        foreach (var filePath in ppkFilePaths)
        {
            if (!_ppkConverter.IsPpkFile(filePath))
            {
                invalid.Add(filePath);
                _logger.LogWarning("Not a valid PPK file: {Path}", filePath);
                continue;
            }

            var info = await _ppkConverter.GetPpkInfoAsync(filePath, ct);

            if (!string.IsNullOrEmpty(info.ErrorMessage))
            {
                invalid.Add(filePath);
                _logger.LogError("Error reading PPK file {Path}: {Error}", filePath, info.ErrorMessage);
                continue;
            }

            if (info.IsEncrypted)
            {
                needsPassphrase.Add(filePath);
                _logger.LogInformation("Encrypted ({KeyType} v{Version}): {Path}",
                    info.KeyType, info.Version, filePath);
            }
            else
            {
                unencrypted.Add(filePath);
                _logger.LogInformation("Unencrypted ({KeyType} v{Version}): {Path}",
                    info.KeyType, info.Version, filePath);
            }
        }

        _logger.LogInformation("Validation Summary:");
        _logger.LogInformation("  Unencrypted (ready to convert): {Count}", unencrypted.Count);
        _logger.LogInformation("  Encrypted (need passphrase): {Count}", needsPassphrase.Count);
        _logger.LogInformation("  Invalid/Error: {Count}", invalid.Count);

        return new
        {
            Unencrypted = unencrypted,
            NeedsPassphrase = needsPassphrase,
            Invalid = invalid
        };
    }

    /// <summary>
    /// Example 5: Convert with cancellation support (useful for long-running batch operations).
    /// </summary>
    public async Task ConvertWithCancellationAsync(
        IEnumerable<(string ppkPath, string? passphrase)> ppkFiles,
        string outputDirectory,
        IProgress<(int current, int total)>? progress = null)
    {
        using var cts = new CancellationTokenSource();

        // Cancel after 5 minutes
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var result = await _ppkConverter.ConvertBatchAndSaveAsync(
                ppkFiles,
                outputDirectory,
                cts.Token);

            _logger.LogInformation("Completed: {Success}/{Total} files converted successfully",
                result.SuccessCount, result.TotalCount);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch conversion was cancelled");
            throw;
        }
    }
}
