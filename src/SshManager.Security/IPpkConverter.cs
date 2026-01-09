namespace SshManager.Security;

/// <summary>
/// Service for converting PuTTY Private Key (PPK) files to OpenSSH format.
/// </summary>
public interface IPpkConverter
{
    /// <summary>
    /// Converts a PPK file to OpenSSH format.
    /// </summary>
    /// <param name="ppkPath">Path to the PPK file.</param>
    /// <param name="passphrase">Optional passphrase if the key is encrypted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Conversion result with OpenSSH private and public keys.</returns>
    Task<PpkConversionResult> ConvertToOpenSshAsync(
        string ppkPath,
        string? passphrase = null,
        CancellationToken ct = default);

    /// <summary>
    /// Converts a PPK file and saves the result to OpenSSH format files.
    /// </summary>
    /// <param name="ppkPath">Path to the PPK file.</param>
    /// <param name="outputPath">Path for the output private key file (public key will be outputPath.pub).</param>
    /// <param name="passphrase">Optional passphrase if the key is encrypted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Path to the saved private key file.</returns>
    Task<string> ConvertAndSaveAsync(
        string ppkPath,
        string outputPath,
        string? passphrase = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a file is a PPK file by examining its header.
    /// </summary>
    /// <param name="filePath">Path to the file to check.</param>
    /// <returns>True if the file is a PPK file.</returns>
    bool IsPpkFile(string filePath);

    /// <summary>
    /// Gets information about a PPK file without converting it.
    /// </summary>
    /// <param name="ppkPath">Path to the PPK file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about the PPK file.</returns>
    Task<PpkFileInfo> GetPpkInfoAsync(string ppkPath, CancellationToken ct = default);

    /// <summary>
    /// Converts multiple PPK files to OpenSSH format in a batch operation.
    /// Each file is processed independently - one failure does not stop the batch.
    /// </summary>
    /// <param name="ppkFiles">Collection of PPK file paths with their optional passphrases.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Batch conversion results including success/failure status for each file.</returns>
    Task<BatchPpkConversionResult> ConvertBatchToOpenSshAsync(
        IEnumerable<(string ppkPath, string? passphrase)> ppkFiles,
        CancellationToken ct = default);

    /// <summary>
    /// Converts multiple PPK files and saves them to a specified directory.
    /// Each file is processed independently - one failure does not stop the batch.
    /// </summary>
    /// <param name="ppkFiles">Collection of PPK file paths with their optional passphrases.</param>
    /// <param name="outputDirectory">Directory where converted files will be saved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Batch conversion results including saved file paths for successful conversions.</returns>
    Task<BatchPpkConversionResult> ConvertBatchAndSaveAsync(
        IEnumerable<(string ppkPath, string? passphrase)> ppkFiles,
        string outputDirectory,
        CancellationToken ct = default);

    /// <summary>
    /// Converts an OpenSSH private key to PPK format.
    /// </summary>
    /// <param name="openSshPrivateKeyPath">Path to the OpenSSH private key file.</param>
    /// <param name="sourcePassphrase">Optional passphrase if the OpenSSH key is encrypted.</param>
    /// <param name="outputPassphrase">Optional passphrase to encrypt the PPK file.</param>
    /// <param name="version">PPK version to create (V2 or V3). Default is V3.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Conversion result with PPK file content.</returns>
    Task<OpenSshToPpkResult> ConvertToPpkAsync(
        string openSshPrivateKeyPath,
        string? sourcePassphrase = null,
        string? outputPassphrase = null,
        PpkVersion version = PpkVersion.V3,
        CancellationToken ct = default);

    /// <summary>
    /// Converts an OpenSSH private key to PPK format and saves it to a file.
    /// </summary>
    /// <param name="openSshPrivateKeyPath">Path to the OpenSSH private key file.</param>
    /// <param name="ppkOutputPath">Path where the PPK file will be saved.</param>
    /// <param name="sourcePassphrase">Optional passphrase if the OpenSSH key is encrypted.</param>
    /// <param name="outputPassphrase">Optional passphrase to encrypt the PPK file.</param>
    /// <param name="version">PPK version to create (V2 or V3). Default is V3.</param>
    /// <param name="comment">Optional comment for the PPK file. If null, preserves original comment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Path to the saved PPK file.</returns>
    Task<string> ConvertAndSaveAsPpkAsync(
        string openSshPrivateKeyPath,
        string ppkOutputPath,
        string? sourcePassphrase = null,
        string? outputPassphrase = null,
        PpkVersion version = PpkVersion.V3,
        string? comment = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a file is an OpenSSH private key by examining its header.
    /// </summary>
    /// <param name="filePath">Path to the file to check.</param>
    /// <returns>True if the file is an OpenSSH private key.</returns>
    bool IsOpenSshPrivateKey(string filePath);
}

/// <summary>
/// Result of a PPK to OpenSSH conversion.
/// </summary>
/// <param name="Success">Whether the conversion was successful.</param>
/// <param name="OpenSshPrivateKey">The converted private key in OpenSSH format (null on failure).</param>
/// <param name="OpenSshPublicKey">The converted public key in OpenSSH format (null on failure).</param>
/// <param name="Fingerprint">SHA256 fingerprint of the public key (null on failure).</param>
/// <param name="KeyType">Key type (e.g., "ssh-rsa", "ssh-ed25519").</param>
/// <param name="Comment">Comment from the PPK file.</param>
/// <param name="ErrorMessage">Error message if conversion failed.</param>
public record PpkConversionResult(
    bool Success,
    string? OpenSshPrivateKey,
    string? OpenSshPublicKey,
    string? Fingerprint,
    string? KeyType,
    string? Comment,
    string? ErrorMessage);

/// <summary>
/// Information about a PPK file.
/// </summary>
/// <param name="Version">PPK format version (2 or 3).</param>
/// <param name="KeyType">Key algorithm type (e.g., "ssh-rsa", "ssh-ed25519").</param>
/// <param name="Comment">Comment embedded in the PPK file.</param>
/// <param name="IsEncrypted">Whether the private key is encrypted with a passphrase.</param>
/// <param name="ErrorMessage">Error message if parsing failed.</param>
public record PpkFileInfo(
    int Version,
    string KeyType,
    string? Comment,
    bool IsEncrypted,
    string? ErrorMessage);

/// <summary>
/// Result of a batch PPK to OpenSSH conversion operation.
/// </summary>
/// <param name="TotalCount">Total number of files processed.</param>
/// <param name="SuccessCount">Number of files converted successfully.</param>
/// <param name="FailureCount">Number of files that failed to convert.</param>
/// <param name="Results">Detailed results for each file in the batch.</param>
public record BatchPpkConversionResult(
    int TotalCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<PpkBatchItemResult> Results);

/// <summary>
/// Result of converting a single PPK file in a batch operation.
/// </summary>
/// <param name="PpkPath">Path to the source PPK file.</param>
/// <param name="Success">Whether the conversion was successful.</param>
/// <param name="ConversionResult">The conversion result (null on failure).</param>
/// <param name="SavedPath">Path where the converted private key was saved (null if not saved).</param>
/// <param name="ErrorMessage">Error message if conversion failed.</param>
public record PpkBatchItemResult(
    string PpkPath,
    bool Success,
    PpkConversionResult? ConversionResult,
    string? SavedPath,
    string? ErrorMessage);

/// <summary>
/// PPK file format version.
/// </summary>
public enum PpkVersion
{
    /// <summary>
    /// PPK version 2 (legacy format using SHA1-based key derivation).
    /// </summary>
    V2 = 2,

    /// <summary>
    /// PPK version 3 (modern format using Argon2 key derivation).
    /// </summary>
    V3 = 3
}

/// <summary>
/// Result of an OpenSSH to PPK conversion.
/// </summary>
/// <param name="Success">Whether the conversion was successful.</param>
/// <param name="PpkContent">The full PPK file content (null on failure).</param>
/// <param name="KeyType">Key type (e.g., "ssh-rsa", "ssh-ed25519").</param>
/// <param name="Comment">Comment from the key file.</param>
/// <param name="ErrorMessage">Error message if conversion failed.</param>
public record OpenSshToPpkResult(
    bool Success,
    string? PpkContent,
    string? KeyType,
    string? Comment,
    string? ErrorMessage);
