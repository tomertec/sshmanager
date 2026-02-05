using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;

namespace SshManager.Security;

/// <summary>
/// Converts PuTTY Private Key (PPK) files to OpenSSH format.
/// Supports PPK version 2 (legacy) and version 3 (modern with Argon2).
/// </summary>
public sealed class PpkConverter : IPpkConverter
{
    private readonly ILogger<PpkConverter> _logger;
    private readonly ISshKeyManager _keyManager;

    public PpkConverter(ISshKeyManager keyManager, ILogger<PpkConverter>? logger = null)
    {
        _keyManager = keyManager;
        _logger = logger ?? NullLogger<PpkConverter>.Instance;
    }

    public bool IsPpkFile(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine();
            return firstLine?.StartsWith("PuTTY-User-Key-File-", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PpkFileInfo> GetPpkInfoAsync(string ppkPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var ppk = ParsePpkFile(ppkPath);
                return new PpkFileInfo(
                    Version: ppk.Version,
                    KeyType: ppk.KeyType,
                    Comment: ppk.Comment,
                    IsEncrypted: ppk.IsEncrypted,
                    ErrorMessage: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse PPK file info: {Path}", ppkPath);
                return new PpkFileInfo(
                    Version: 0,
                    KeyType: "Unknown",
                    Comment: null,
                    IsEncrypted: false,
                    ErrorMessage: ex.Message);
            }
        }, ct);
    }

    public async Task<PpkConversionResult> ConvertToOpenSshAsync(
        string ppkPath,
        string? passphrase = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Converting PPK file to OpenSSH format: {Path}", ppkPath);

                // Parse PPK file
                var ppk = ParsePpkFile(ppkPath);

                // Decrypt private key if encrypted, always validate MAC
                byte[] privateKeyBlob;
                if (ppk.IsEncrypted)
                {
                    privateKeyBlob = DecryptPrivateKey(ppk, passphrase ?? string.Empty);
                }
                else
                {
                    // Even unencrypted files have a MAC that must be validated
                    ValidateUnencryptedMac(ppk);
                    privateKeyBlob = ppk.PrivateKeyBlob;
                }

                // Convert to OpenSSH format using SSH.NET
                var (openSshPrivateKey, openSshPublicKey) = ConvertToOpenSshFormat(
                    ppk.KeyType,
                    ppk.PublicKeyBlob,
                    privateKeyBlob,
                    ppk.Comment);

                // Compute fingerprint
                var fingerprint = _keyManager.ComputeFingerprint(openSshPublicKey);

                _logger.LogInformation("Successfully converted PPK to OpenSSH format: {KeyType}", ppk.KeyType);

                return new PpkConversionResult(
                    Success: true,
                    OpenSshPrivateKey: openSshPrivateKey,
                    OpenSshPublicKey: openSshPublicKey,
                    Fingerprint: fingerprint,
                    KeyType: ppk.KeyType,
                    Comment: ppk.Comment,
                    ErrorMessage: null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert PPK file: {Path}", ppkPath);
                return new PpkConversionResult(
                    Success: false,
                    OpenSshPrivateKey: null,
                    OpenSshPublicKey: null,
                    Fingerprint: null,
                    KeyType: null,
                    Comment: null,
                    ErrorMessage: ex.Message);
            }
        }, ct);
    }

    public async Task<string> ConvertAndSaveAsync(
        string ppkPath,
        string outputPath,
        string? passphrase = null,
        CancellationToken ct = default)
    {
        var result = await ConvertToOpenSshAsync(ppkPath, passphrase, ct);

        if (!result.Success || result.OpenSshPrivateKey == null || result.OpenSshPublicKey == null)
        {
            throw new InvalidOperationException(
                $"Failed to convert PPK file: {result.ErrorMessage}");
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save private key
            File.WriteAllText(outputPath, result.OpenSshPrivateKey);
            _logger.LogInformation("Saved OpenSSH private key to: {Path}", outputPath);

            // Save public key
            var publicKeyPath = outputPath + ".pub";
            File.WriteAllText(publicKeyPath, result.OpenSshPublicKey);
            _logger.LogInformation("Saved OpenSSH public key to: {Path}", publicKeyPath);
        }, ct);

        return outputPath;
    }

    public async Task<BatchPpkConversionResult> ConvertBatchToOpenSshAsync(
        IEnumerable<(string ppkPath, string? passphrase)> ppkFiles,
        CancellationToken ct = default)
    {
        var filesList = ppkFiles.ToList();
        _logger.LogInformation("Starting batch conversion of {Count} PPK files", filesList.Count);

        var results = new List<PpkBatchItemResult>();
        var successCount = 0;
        var failureCount = 0;

        foreach (var (ppkPath, passphrase) in filesList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug("Converting file: {Path}", ppkPath);

                var conversionResult = await ConvertToOpenSshAsync(ppkPath, passphrase, ct);

                if (conversionResult.Success)
                {
                    successCount++;
                    _logger.LogInformation("Successfully converted: {Path}", ppkPath);
                }
                else
                {
                    failureCount++;
                    _logger.LogWarning("Failed to convert {Path}: {Error}", ppkPath, conversionResult.ErrorMessage);
                }

                results.Add(new PpkBatchItemResult(
                    PpkPath: ppkPath,
                    Success: conversionResult.Success,
                    ConversionResult: conversionResult,
                    SavedPath: null,
                    ErrorMessage: conversionResult.ErrorMessage));
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "Exception while converting {Path}", ppkPath);

                results.Add(new PpkBatchItemResult(
                    PpkPath: ppkPath,
                    Success: false,
                    ConversionResult: null,
                    SavedPath: null,
                    ErrorMessage: ex.Message));
            }
        }

        _logger.LogInformation(
            "Batch conversion completed: {Success} succeeded, {Failure} failed out of {Total} total",
            successCount, failureCount, filesList.Count);

        return new BatchPpkConversionResult(
            TotalCount: filesList.Count,
            SuccessCount: successCount,
            FailureCount: failureCount,
            Results: results);
    }

    public async Task<BatchPpkConversionResult> ConvertBatchAndSaveAsync(
        IEnumerable<(string ppkPath, string? passphrase)> ppkFiles,
        string outputDirectory,
        CancellationToken ct = default)
    {
        var filesList = ppkFiles.ToList();
        _logger.LogInformation(
            "Starting batch conversion and save of {Count} PPK files to directory: {Directory}",
            filesList.Count, outputDirectory);

        // Ensure output directory exists
        await Task.Run(() =>
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                _logger.LogInformation("Created output directory: {Directory}", outputDirectory);
            }
        }, ct);

        var results = new List<PpkBatchItemResult>();
        var successCount = 0;
        var failureCount = 0;

        foreach (var (ppkPath, passphrase) in filesList)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug("Converting and saving file: {Path}", ppkPath);

                // Convert the file
                var conversionResult = await ConvertToOpenSshAsync(ppkPath, passphrase, ct);

                if (!conversionResult.Success)
                {
                    failureCount++;
                    _logger.LogWarning("Failed to convert {Path}: {Error}", ppkPath, conversionResult.ErrorMessage);

                    results.Add(new PpkBatchItemResult(
                        PpkPath: ppkPath,
                        Success: false,
                        ConversionResult: conversionResult,
                        SavedPath: null,
                        ErrorMessage: conversionResult.ErrorMessage));
                    continue;
                }

                // Generate output file path based on source file name
                var fileName = Path.GetFileNameWithoutExtension(ppkPath);
                var outputPath = Path.Combine(outputDirectory, fileName);

                // Ensure unique filename if file already exists
                var counter = 1;
                var uniqueOutputPath = outputPath;
                while (File.Exists(uniqueOutputPath) || File.Exists(uniqueOutputPath + ".pub"))
                {
                    uniqueOutputPath = $"{outputPath}_{counter}";
                    counter++;
                }

                // Save the converted keys
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    // Save private key
                    File.WriteAllText(uniqueOutputPath, conversionResult.OpenSshPrivateKey!);
                    _logger.LogDebug("Saved private key to: {Path}", uniqueOutputPath);

                    // Save public key
                    var publicKeyPath = uniqueOutputPath + ".pub";
                    File.WriteAllText(publicKeyPath, conversionResult.OpenSshPublicKey!);
                    _logger.LogDebug("Saved public key to: {Path}", publicKeyPath);
                }, ct);

                successCount++;
                _logger.LogInformation("Successfully converted and saved: {Source} -> {Destination}",
                    ppkPath, uniqueOutputPath);

                results.Add(new PpkBatchItemResult(
                    PpkPath: ppkPath,
                    Success: true,
                    ConversionResult: conversionResult,
                    SavedPath: uniqueOutputPath,
                    ErrorMessage: null));
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "Exception while converting and saving {Path}", ppkPath);

                results.Add(new PpkBatchItemResult(
                    PpkPath: ppkPath,
                    Success: false,
                    ConversionResult: null,
                    SavedPath: null,
                    ErrorMessage: ex.Message));
            }
        }

        _logger.LogInformation(
            "Batch conversion and save completed: {Success} succeeded, {Failure} failed out of {Total} total",
            successCount, failureCount, filesList.Count);

        return new BatchPpkConversionResult(
            TotalCount: filesList.Count,
            SuccessCount: successCount,
            FailureCount: failureCount,
            Results: results);
    }

    private PpkFile ParsePpkFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var lineIndex = 0;

        // Parse header: PuTTY-User-Key-File-2: ssh-rsa
        if (lineIndex >= lines.Length)
            throw new FormatException("Empty PPK file");

        var headerParts = lines[lineIndex++].Split(':', 2);
        if (headerParts.Length != 2 || !headerParts[0].StartsWith("PuTTY-User-Key-File-"))
            throw new FormatException("Invalid PPK file header");

        var versionStr = headerParts[0].Replace("PuTTY-User-Key-File-", "").Trim();
        if (!int.TryParse(versionStr, out var version) || (version != 2 && version != 3))
            throw new FormatException($"Unsupported PPK version: {versionStr}");

        var keyType = headerParts[1].Trim();

        // Parse encryption
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Encryption:"))
            throw new FormatException("Missing Encryption field");

        var encryption = lines[lineIndex++].Split(':', 2)[1].Trim();
        var isEncrypted = encryption != "none";

        // Parse comment
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Comment:"))
            throw new FormatException("Missing Comment field");

        var comment = lines[lineIndex++].Split(':', 2)[1].Trim();

        // Parse public key
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Public-Lines:"))
            throw new FormatException("Missing Public-Lines field");

        var publicLines = int.Parse(lines[lineIndex++].Split(':')[1].Trim());
        var publicKeyBase64 = string.Join("", lines.Skip(lineIndex).Take(publicLines));
        lineIndex += publicLines;

        var publicKeyBlob = Convert.FromBase64String(publicKeyBase64);

        // Parse private key
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Private-Lines:"))
            throw new FormatException("Missing Private-Lines field");

        var privateLines = int.Parse(lines[lineIndex++].Split(':')[1].Trim());
        var privateKeyBase64 = string.Join("", lines.Skip(lineIndex).Take(privateLines));
        lineIndex += privateLines;

        var privateKeyBlob = Convert.FromBase64String(privateKeyBase64);

        // Parse MAC
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Private-MAC:"))
            throw new FormatException("Missing Private-MAC field");

        var mac = lines[lineIndex++].Split(':', 2)[1].Trim();

        // Parse Argon2 parameters for v3
        PpkArgon2Parameters? argon2Params = null;
        if (version == 3 && isEncrypted)
        {
            argon2Params = ParseArgon2Parameters(lines, ref lineIndex);
        }

        return new PpkFile(
            Version: version,
            KeyType: keyType,
            Encryption: encryption,
            IsEncrypted: isEncrypted,
            Comment: comment,
            PublicKeyBlob: publicKeyBlob,
            PrivateKeyBlob: privateKeyBlob,
            Mac: mac,
            Argon2Params: argon2Params);
    }

    private PpkArgon2Parameters ParseArgon2Parameters(string[] lines, ref int lineIndex)
    {
        // PPK v3 includes: Key-Derivation, Argon2-Memory, Argon2-Passes, Argon2-Parallelism, Argon2-Salt
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Key-Derivation:"))
            throw new FormatException("Missing Key-Derivation field for PPK v3");

        var keyDerivation = lines[lineIndex++].Split(':')[1].Trim();
        if (keyDerivation != "Argon2id" && keyDerivation != "Argon2i" && keyDerivation != "Argon2d")
            throw new FormatException($"Unsupported key derivation: {keyDerivation}");

        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Argon2-Memory:"))
            throw new FormatException("Missing Argon2-Memory field");
        var memory = int.Parse(lines[lineIndex++].Split(':')[1].Trim());

        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Argon2-Passes:"))
            throw new FormatException("Missing Argon2-Passes field");
        var passes = int.Parse(lines[lineIndex++].Split(':')[1].Trim());

        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Argon2-Parallelism:"))
            throw new FormatException("Missing Argon2-Parallelism field");
        var parallelism = int.Parse(lines[lineIndex++].Split(':')[1].Trim());

        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("Argon2-Salt:"))
            throw new FormatException("Missing Argon2-Salt field");
        var saltHex = lines[lineIndex++].Split(':')[1].Trim();
        var salt = Convert.FromHexString(saltHex);

        return new PpkArgon2Parameters(
            Flavor: keyDerivation,
            Memory: memory,
            Passes: passes,
            Parallelism: parallelism,
            Salt: salt);
    }

    private byte[] DecryptPrivateKey(PpkFile ppk, string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase required for encrypted PPK file");

        _logger.LogDebug("Decrypting PPK v{Version} private key", ppk.Version);

        byte[] key;
        byte[] iv;
        byte[] macKey;

        if (ppk.Version == 3)
        {
            // PPK v3 uses Argon2 for key derivation
            if (ppk.Argon2Params == null)
                throw new InvalidOperationException("Missing Argon2 parameters for PPK v3");

            var keyMaterial = DeriveKeyArgon2(
                passphrase,
                ppk.Argon2Params.Salt,
                ppk.Argon2Params.Memory,
                ppk.Argon2Params.Passes,
                ppk.Argon2Params.Parallelism,
                ppk.Argon2Params.Flavor,
                outputLength: 80); // 32 AES key + 16 IV + 32 MAC key

            // First 32 bytes for AES key, next 16 for IV, last 32 for MAC key
            key = keyMaterial[..32];
            iv = keyMaterial[32..48];
            macKey = keyMaterial[48..80];
        }
        else
        {
            // PPK v2 uses SHA1 for key derivation
            key = DeriveKeyPpkV2(passphrase, ppk.KeyType);
            iv = new byte[16]; // PPK v2 uses zero IV
            macKey = DeriveMacKeyPpkV2(passphrase);
        }

        // Validate MAC BEFORE decryption to detect tampering or wrong passphrase
        ValidateMac(ppk, macKey);

        // Decrypt using AES-256-CBC (v3) or AES-256-CBC (v2)
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // PPK uses custom padding

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(ppk.PrivateKeyBlob, 0, ppk.PrivateKeyBlob.Length);

        return decrypted;
    }

    /// <summary>
    /// Validates the Private-MAC to verify integrity and correct passphrase.
    /// This MUST be called before using decrypted key material.
    /// </summary>
    private void ValidateMac(PpkFile ppk, byte[] macKey)
    {
        // Build the MAC data: key-type, encryption, comment, public-key, private-key
        // Each field is prefixed with its length as a 4-byte big-endian integer
        using var macDataStream = new MemoryStream();
        using var macWriter = new BinaryWriter(macDataStream);

        // Write key type
        var keyTypeBytes = Encoding.UTF8.GetBytes(ppk.KeyType);
        WriteUInt32(macWriter, (uint)keyTypeBytes.Length);
        macWriter.Write(keyTypeBytes);

        // Write encryption
        var encryptionBytes = Encoding.UTF8.GetBytes(ppk.Encryption);
        WriteUInt32(macWriter, (uint)encryptionBytes.Length);
        macWriter.Write(encryptionBytes);

        // Write comment
        var commentBytes = Encoding.UTF8.GetBytes(ppk.Comment ?? string.Empty);
        WriteUInt32(macWriter, (uint)commentBytes.Length);
        macWriter.Write(commentBytes);

        // Write public key blob
        WriteUInt32(macWriter, (uint)ppk.PublicKeyBlob.Length);
        macWriter.Write(ppk.PublicKeyBlob);

        // Write private key blob (encrypted form)
        WriteUInt32(macWriter, (uint)ppk.PrivateKeyBlob.Length);
        macWriter.Write(ppk.PrivateKeyBlob);

        var macData = macDataStream.ToArray();

        // Compute expected MAC based on version
        byte[] expectedMac;
        if (ppk.Version == 3)
        {
            // PPK v3 uses HMAC-SHA256
            using var hmac = new HMACSHA256(macKey);
            expectedMac = hmac.ComputeHash(macData);
        }
        else
        {
            // PPK v2 uses HMAC-SHA1
            using var hmac = new HMACSHA1(macKey);
            expectedMac = hmac.ComputeHash(macData);
        }

        // Convert expected MAC to hex string for comparison
        var expectedMacHex = Convert.ToHexString(expectedMac).ToLowerInvariant();
        var actualMacHex = ppk.Mac.ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedMacHex),
            Encoding.ASCII.GetBytes(actualMacHex)))
        {
            _logger.LogWarning("PPK MAC validation failed - possible tampering or wrong passphrase");
            throw new CryptographicException(
                "PPK file MAC validation failed. The file may be corrupted, tampered with, " +
                "or the passphrase may be incorrect.");
        }

        _logger.LogDebug("PPK MAC validation successful");
    }

    /// <summary>
    /// Derives the MAC key for PPK v2 files.
    /// The key is SHA1("putty-private-key-file-mac-key" + passphrase).
    /// </summary>
    private byte[] DeriveMacKeyPpkV2(string passphrase)
    {
        using var sha1 = SHA1.Create();
        var input = Encoding.UTF8.GetBytes("putty-private-key-file-mac-key" + passphrase);
        return sha1.ComputeHash(input);
    }

    /// <summary>
    /// Validates the MAC for unencrypted PPK files.
    /// Unencrypted files use empty passphrase for MAC derivation.
    /// </summary>
    private void ValidateUnencryptedMac(PpkFile ppk)
    {
        byte[] macKey;
        if (ppk.Version == 3)
        {
            // PPK v3 unencrypted uses a 32-byte zero key for HMAC-SHA256
            macKey = new byte[32];
        }
        else
        {
            // PPK v2 unencrypted uses SHA1("putty-private-key-file-mac-key")
            macKey = DeriveMacKeyPpkV2(string.Empty);
        }

        ValidateMac(ppk, macKey);
    }

    private byte[] DeriveKeyArgon2(
        string passphrase,
        byte[] salt,
        int memoryKiB,
        int iterations,
        int parallelism,
        string flavor,
        int outputLength = 48)
    {
        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);

        Argon2 argon2 = flavor switch
        {
            "Argon2id" => new Argon2id(passphraseBytes),
            "Argon2i" => new Argon2i(passphraseBytes),
            "Argon2d" => new Argon2d(passphraseBytes),
            _ => throw new NotSupportedException($"Unsupported Argon2 flavor: {flavor}")
        };

        try
        {
            argon2.Salt = salt;
            argon2.DegreeOfParallelism = parallelism;
            argon2.Iterations = iterations;
            argon2.MemorySize = memoryKiB;

            return argon2.GetBytes(outputLength);
        }
        finally
        {
            argon2.Dispose();
        }
    }

    private byte[] DeriveKeyPpkV2(string passphrase, string keyType)
    {
        // PPK v2 key derivation: SHA1(sequence || passphrase) where sequence is 0,1,2...
        // We need 32 bytes for AES-256, so SHA1(0||pass) || SHA1(1||pass) = 40 bytes, take first 32
        using var sha1 = SHA1.Create();

        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
        var result = new byte[32];

        for (int i = 0; i < 2; i++)
        {
            var input = new byte[4 + passphraseBytes.Length];
            BitConverter.GetBytes(i).CopyTo(input, 0);
            passphraseBytes.CopyTo(input, 4);

            var hash = sha1.ComputeHash(input);
            var copyLength = Math.Min(hash.Length, result.Length - (i * hash.Length));
            Array.Copy(hash, 0, result, i * hash.Length, copyLength);
        }

        return result;
    }

    private (string privateKey, string publicKey) ConvertToOpenSshFormat(
        string keyType,
        byte[] publicKeyBlob,
        byte[] privateKeyBlob,
        string? comment)
    {
        _logger.LogDebug("Converting {KeyType} to OpenSSH format", keyType);

        // Use SSH.NET to parse and re-export the key
        try
        {
            // Create a temporary PPK-style file that SSH.NET can parse
            // Actually, SSH.NET expects OpenSSH format, so we need to manually construct it

            // For now, use SSH.NET's PrivateKeyFile to handle the conversion
            // We'll need to construct the key data in a format SSH.NET understands

            // Parse public key to get the OpenSSH format
            var publicKey = FormatPublicKey(keyType, publicKeyBlob, comment);

            // For private key, we need to write it in OpenSSH private key format
            // This is complex, so we'll use a temporary file approach with SSH.NET
            var privateKey = FormatPrivateKey(keyType, publicKeyBlob, privateKeyBlob);

            return (privateKey, publicKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert key format");
            throw new InvalidOperationException($"Failed to convert {keyType} key to OpenSSH format", ex);
        }
    }

    private string FormatPublicKey(string keyType, byte[] publicKeyBlob, string? comment)
    {
        // Public key is already in SSH wire format
        var base64 = Convert.ToBase64String(publicKeyBlob);
        return string.IsNullOrEmpty(comment)
            ? $"{keyType} {base64}"
            : $"{keyType} {base64} {comment}";
    }

    private string FormatPrivateKey(string keyType, byte[] publicKeyBlob, byte[] privateKeyBlob)
    {
        // OpenSSH private key format (RFC 4716 / OpenSSH format)
        // This is the "-----BEGIN OPENSSH PRIVATE KEY-----" format

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Magic bytes: "openssh-key-v1\0"
        writer.Write(Encoding.ASCII.GetBytes("openssh-key-v1\0"));

        // Cipher name (none for unencrypted)
        WriteString(writer, "none");

        // KDF name (none for unencrypted)
        WriteString(writer, "none");

        // KDF options (empty for none)
        WriteString(writer, new byte[0]);

        // Number of keys (always 1)
        WriteUInt32(writer, 1);

        // Public key blob
        WriteString(writer, publicKeyBlob);

        // Private key section
        using var privateMs = new MemoryStream();
        using var privateWriter = new BinaryWriter(privateMs);

        // Check integers (same random value twice for unencrypted keys)
        var checkInt = RandomNumberGenerator.GetInt32(int.MaxValue);
        WriteUInt32(privateWriter, (uint)checkInt);
        WriteUInt32(privateWriter, (uint)checkInt);

        // Write the actual private key data based on key type
        WritePrivateKeyData(privateWriter, keyType, publicKeyBlob, privateKeyBlob);

        // Padding to block size (8 bytes for OpenSSH format)
        var privateData = privateMs.ToArray();
        var paddingLength = 8 - (privateData.Length % 8);
        for (byte i = 1; i <= paddingLength; i++)
        {
            privateWriter.Write(i);
        }

        privateData = privateMs.ToArray();
        WriteString(writer, privateData);

        // Encode as base64 and wrap in PEM format
        var keyData = ms.ToArray();
        var base64 = Convert.ToBase64String(keyData);

        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN OPENSSH PRIVATE KEY-----");

        // Wrap base64 at 70 characters (OpenSSH standard)
        for (int i = 0; i < base64.Length; i += 70)
        {
            sb.AppendLine(base64.Substring(i, Math.Min(70, base64.Length - i)));
        }

        sb.AppendLine("-----END OPENSSH PRIVATE KEY-----");

        return sb.ToString();
    }

    private void WritePrivateKeyData(
        BinaryWriter writer,
        string keyType,
        byte[] publicKeyBlob,
        byte[] privateKeyBlob)
    {
        // Parse the private key blob based on key type
        using var reader = new BinaryReader(new MemoryStream(privateKeyBlob));

        // Write key type
        WriteString(writer, keyType);

        if (keyType == "ssh-rsa")
        {
            WriteRsaPrivateKey(writer, reader, publicKeyBlob);
        }
        else if (keyType.StartsWith("ecdsa-sha2-"))
        {
            WriteEcdsaPrivateKey(writer, reader, publicKeyBlob, keyType);
        }
        else if (keyType == "ssh-ed25519")
        {
            WriteEd25519PrivateKey(writer, reader, publicKeyBlob);
        }
        else
        {
            throw new NotSupportedException($"Unsupported key type: {keyType}");
        }

        // Comment (empty string)
        WriteString(writer, "");
    }

    private void WriteRsaPrivateKey(BinaryWriter writer, BinaryReader reader, byte[] publicKeyBlob)
    {
        // Public key components from public blob
        using var pubReader = new BinaryReader(new MemoryStream(publicKeyBlob));
        ReadString(pubReader); // key type
        var e = ReadMpint(pubReader);
        var n = ReadMpint(pubReader);

        // Private key components from private blob
        var d = ReadMpint(reader);
        var p = ReadMpint(reader);
        var q = ReadMpint(reader);
        var iqmp = ReadMpint(reader); // inverse of q mod p

        // OpenSSH format: n, e, d, iqmp, p, q
        WriteMpint(writer, n);
        WriteMpint(writer, e);
        WriteMpint(writer, d);
        WriteMpint(writer, iqmp);
        WriteMpint(writer, p);
        WriteMpint(writer, q);
    }

    private void WriteEcdsaPrivateKey(BinaryWriter writer, BinaryReader reader, byte[] publicKeyBlob, string keyType)
    {
        // Public key components
        using var pubReader = new BinaryReader(new MemoryStream(publicKeyBlob));
        ReadString(pubReader); // key type
        var curveName = ReadString(pubReader);
        var publicPoint = ReadString(pubReader);

        // Private key component
        var privateScalar = ReadMpint(reader);

        // OpenSSH format: curve name, public point, private scalar
        WriteString(writer, curveName);
        WriteString(writer, publicPoint);
        WriteMpint(writer, privateScalar);
    }

    private void WriteEd25519PrivateKey(BinaryWriter writer, BinaryReader reader, byte[] publicKeyBlob)
    {
        // Public key component
        using var pubReader = new BinaryReader(new MemoryStream(publicKeyBlob));
        ReadString(pubReader); // key type
        var publicKey = ReadString(pubReader);

        // Private key component (Ed25519 uses 32-byte private key + 32-byte public key concatenated)
        var privateKey = ReadString(reader);

        // OpenSSH format: public key, private key (64 bytes: 32 private + 32 public)
        WriteString(writer, publicKey);

        // Concatenate private + public for Ed25519
        var fullPrivateKey = new byte[64];
        Array.Copy(privateKey, 0, fullPrivateKey, 0, 32);
        Array.Copy(publicKey, 0, fullPrivateKey, 32, 32);
        WriteString(writer, fullPrivateKey);
    }

    private void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteString(writer, bytes);
    }

    private void WriteString(BinaryWriter writer, byte[] data)
    {
        WriteUInt32(writer, (uint)data.Length);
        writer.Write(data);
    }

    private void WriteMpint(BinaryWriter writer, byte[] value)
    {
        // Remove leading zeros except if needed for sign bit
        var start = 0;
        while (start < value.Length - 1 && value[start] == 0 && (value[start + 1] & 0x80) == 0)
        {
            start++;
        }

        var trimmed = value[start..];
        WriteString(writer, trimmed);
    }

    private void WriteUInt32(BinaryWriter writer, uint value)
    {
        // Big-endian
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private byte[] ReadString(BinaryReader reader)
    {
        var length = ReadUInt32(reader);
        return reader.ReadBytes((int)length);
    }

    private byte[] ReadMpint(BinaryReader reader)
    {
        return ReadString(reader);
    }

    private uint ReadUInt32(BinaryReader reader)
    {
        // Big-endian
        var b1 = reader.ReadByte();
        var b2 = reader.ReadByte();
        var b3 = reader.ReadByte();
        var b4 = reader.ReadByte();
        return ((uint)b1 << 24) | ((uint)b2 << 16) | ((uint)b3 << 8) | b4;
    }

    private record PpkFile(
        int Version,
        string KeyType,
        string Encryption,
        bool IsEncrypted,
        string? Comment,
        byte[] PublicKeyBlob,
        byte[] PrivateKeyBlob,
        string Mac,
        PpkArgon2Parameters? Argon2Params);

    private record PpkArgon2Parameters(
        string Flavor,
        int Memory,
        int Passes,
        int Parallelism,
        byte[] Salt);

    public bool IsOpenSshPrivateKey(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            using var reader = new StreamReader(filePath);
            var firstLine = reader.ReadLine();
            return firstLine?.Trim() == "-----BEGIN OPENSSH PRIVATE KEY-----" ||
                   firstLine?.Trim() == "-----BEGIN RSA PRIVATE KEY-----" ||
                   firstLine?.Trim() == "-----BEGIN EC PRIVATE KEY-----";
        }
        catch
        {
            return false;
        }
    }

    public async Task<OpenSshToPpkResult> ConvertToPpkAsync(
        string openSshPrivateKeyPath,
        string? sourcePassphrase = null,
        string? outputPassphrase = null,
        PpkVersion version = PpkVersion.V3,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Converting OpenSSH key to PPK v{Version}: {Path}",
                    (int)version, openSshPrivateKeyPath);

                // Load the OpenSSH private key using SSH.NET
                PrivateKeyFile privateKeyFile;
                if (!string.IsNullOrEmpty(sourcePassphrase))
                {
                    privateKeyFile = new PrivateKeyFile(openSshPrivateKeyPath, sourcePassphrase);
                }
                else
                {
                    privateKeyFile = new PrivateKeyFile(openSshPrivateKeyPath);
                }

                using (privateKeyFile)
                {
                    // Extract key information
                    var keyType = GetSshKeyType(privateKeyFile);
                    var comment = string.Empty; // SSH.NET doesn't expose comment easily

                    // Build public and private key blobs
                    var publicKeyBlob = BuildPublicKeyBlob(privateKeyFile);
                    var privateKeyBlob = BuildPrivateKeyBlob(privateKeyFile);

                    // Build PPK content
                    var ppkContent = BuildPpkContent(
                        version: (int)version,
                        keyType: keyType,
                        comment: comment,
                        publicKeyBlob: publicKeyBlob,
                        privateKeyBlob: privateKeyBlob,
                        passphrase: outputPassphrase);

                    _logger.LogInformation("Successfully converted OpenSSH key to PPK format: {KeyType}", keyType);

                    return new OpenSshToPpkResult(
                        Success: true,
                        PpkContent: ppkContent,
                        KeyType: keyType,
                        Comment: comment,
                        ErrorMessage: null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert OpenSSH key to PPK: {Path}", openSshPrivateKeyPath);
                return new OpenSshToPpkResult(
                    Success: false,
                    PpkContent: null,
                    KeyType: null,
                    Comment: null,
                    ErrorMessage: ex.Message);
            }
        }, ct);
    }

    public async Task<string> ConvertAndSaveAsPpkAsync(
        string openSshPrivateKeyPath,
        string ppkOutputPath,
        string? sourcePassphrase = null,
        string? outputPassphrase = null,
        PpkVersion version = PpkVersion.V3,
        string? comment = null,
        CancellationToken ct = default)
    {
        var result = await ConvertToPpkAsync(
            openSshPrivateKeyPath,
            sourcePassphrase,
            outputPassphrase,
            version,
            ct);

        if (!result.Success || result.PpkContent == null)
        {
            throw new InvalidOperationException(
                $"Failed to convert OpenSSH key to PPK: {result.ErrorMessage}");
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(ppkOutputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save PPK file
            File.WriteAllText(ppkOutputPath, result.PpkContent);
            _logger.LogInformation("Saved PPK file to: {Path}", ppkOutputPath);
        }, ct);

        return ppkOutputPath;
    }

    private string GetSshKeyType(PrivateKeyFile privateKeyFile)
    {
        // Access the first key from the private key file
        // PrivateKeyFile.HostKeyAlgorithms returns a collection of key algorithms
        var keyFiles = (System.Collections.IEnumerable)privateKeyFile
            .GetType()
            .GetProperty("Keys", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .GetValue(privateKeyFile)!;

        foreach (var key in keyFiles)
        {
            var publicKeyData = (byte[])key.GetType()
                .GetProperty("Public", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
                .GetValue(key)!;

            using var reader = new BinaryReader(new MemoryStream(publicKeyData));
            var keyTypeBytes = ReadString(reader);
            return Encoding.UTF8.GetString(keyTypeBytes);
        }

        throw new InvalidOperationException("No keys found in private key file");
    }

    private byte[] BuildPublicKeyBlob(PrivateKeyFile privateKeyFile)
    {
        // Access the first key's public data
        var keyFiles = (System.Collections.IEnumerable)privateKeyFile
            .GetType()
            .GetProperty("Keys", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .GetValue(privateKeyFile)!;

        foreach (var key in keyFiles)
        {
            var publicKeyData = (byte[])key.GetType()
                .GetProperty("Public", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
                .GetValue(key)!;

            return publicKeyData;
        }

        throw new InvalidOperationException("No keys found in private key file");
    }

    private byte[] BuildPrivateKeyBlob(PrivateKeyFile privateKeyFile)
    {
        // Access the first key and extract private components
        var keyFiles = (System.Collections.IEnumerable)privateKeyFile
            .GetType()
            .GetProperty("Keys", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .GetValue(privateKeyFile)!;

        foreach (var key in keyFiles)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var keyTypeName = key.GetType().Name;

            if (keyTypeName.Contains("Rsa", StringComparison.OrdinalIgnoreCase))
            {
                WritePpkRsaPrivateKey(writer, key);
            }
            else if (keyTypeName.Contains("Ecdsa", StringComparison.OrdinalIgnoreCase))
            {
                WritePpkEcdsaPrivateKey(writer, key);
            }
            else if (keyTypeName.Contains("ED25519", StringComparison.OrdinalIgnoreCase) ||
                     keyTypeName.Contains("Ed25519", StringComparison.OrdinalIgnoreCase))
            {
                WritePpkEd25519PrivateKey(writer, key);
            }
            else
            {
                throw new NotSupportedException($"Unsupported key type: {keyTypeName}");
            }

            return ms.ToArray();
        }

        throw new InvalidOperationException("No keys found in private key file");
    }

    private void WritePpkRsaPrivateKey(BinaryWriter writer, object rsaKey)
    {
        // PPK RSA private key format: d, p, q, iqmp (inverse of q mod p)
        var keyType = rsaKey.GetType();

        // Try to get the parameters via properties/fields
        var dProperty = keyType.GetProperty("D", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var pProperty = keyType.GetProperty("P", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var qProperty = keyType.GetProperty("Q", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var inverseQProperty = keyType.GetProperty("InverseQ", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (dProperty == null || pProperty == null || qProperty == null || inverseQProperty == null)
        {
            throw new InvalidOperationException("Cannot extract RSA private key parameters from SSH.NET key object");
        }

        var d = (System.Numerics.BigInteger)dProperty.GetValue(rsaKey)!;
        var p = (System.Numerics.BigInteger)pProperty.GetValue(rsaKey)!;
        var q = (System.Numerics.BigInteger)qProperty.GetValue(rsaKey)!;
        var iqmp = (System.Numerics.BigInteger)inverseQProperty.GetValue(rsaKey)!;

        // Write as mpints
        WriteMpint(writer, d.ToByteArray(isUnsigned: false, isBigEndian: true));
        WriteMpint(writer, p.ToByteArray(isUnsigned: false, isBigEndian: true));
        WriteMpint(writer, q.ToByteArray(isUnsigned: false, isBigEndian: true));
        WriteMpint(writer, iqmp.ToByteArray(isUnsigned: false, isBigEndian: true));
    }

    private void WritePpkEcdsaPrivateKey(BinaryWriter writer, object ecdsaKey)
    {
        // PPK ECDSA private key format: private scalar (big integer)
        var keyType = ecdsaKey.GetType();

        var dProperty = keyType.GetProperty("D", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (dProperty == null)
        {
            throw new InvalidOperationException("Cannot extract ECDSA private key parameter from SSH.NET key object");
        }

        var d = (System.Numerics.BigInteger)dProperty.GetValue(ecdsaKey)!;

        // Write as mpint
        WriteMpint(writer, d.ToByteArray(isUnsigned: false, isBigEndian: true));
    }

    private void WritePpkEd25519PrivateKey(BinaryWriter writer, object ed25519Key)
    {
        // PPK Ed25519 private key format: 32-byte private key as a string (length-prefixed)
        var keyType = ed25519Key.GetType();

        var privateKeyProperty = keyType.GetProperty("PrivateKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (privateKeyProperty == null)
        {
            throw new InvalidOperationException("Cannot extract Ed25519 private key from SSH.NET key object");
        }

        var privateKey = (byte[])privateKeyProperty.GetValue(ed25519Key)!;

        // Ed25519 private keys are 32 bytes, write as SSH string
        WriteString(writer, privateKey[..32]); // Ensure exactly 32 bytes
    }

    private string BuildPpkContent(
        int version,
        string keyType,
        string comment,
        byte[] publicKeyBlob,
        byte[] privateKeyBlob,
        string? passphrase)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"PuTTY-User-Key-File-{version}: {keyType}");

        // Encryption
        var isEncrypted = !string.IsNullOrEmpty(passphrase);
        var encryption = isEncrypted ? "aes256-cbc" : "none";
        sb.AppendLine($"Encryption: {encryption}");

        // Comment
        sb.AppendLine($"Comment: {comment}");

        // Public key
        var publicKeyBase64 = Convert.ToBase64String(publicKeyBlob);
        var publicKeyLines = SplitIntoLines(publicKeyBase64, 64);
        sb.AppendLine($"Public-Lines: {publicKeyLines.Count}");
        foreach (var line in publicKeyLines)
        {
            sb.AppendLine(line);
        }

        // Prepare private key blob (encrypted or not)
        byte[] finalPrivateKeyBlob;
        byte[] macKey;
        PpkArgon2Parameters? argon2Params = null;

        if (isEncrypted)
        {
            if (version == 3)
            {
                // Generate Argon2 parameters
                var salt = new byte[32];
                RandomNumberGenerator.Fill(salt);

                argon2Params = new PpkArgon2Parameters(
                    Flavor: "Argon2id",
                    Memory: 8192,
                    Passes: 13,
                    Parallelism: 1,
                    Salt: salt);

                var keyMaterial = DeriveKeyArgon2(
                    passphrase!,
                    salt,
                    argon2Params.Memory,
                    argon2Params.Passes,
                    argon2Params.Parallelism,
                    argon2Params.Flavor,
                    outputLength: 80); // 32 AES key + 16 IV + 32 MAC key

                var aesKey = keyMaterial[..32];
                var iv = keyMaterial[32..48];
                macKey = keyMaterial[48..80];

                // Encrypt private key
                finalPrivateKeyBlob = EncryptPrivateKeyBlob(privateKeyBlob, aesKey, iv);

                // Write Argon2 parameters
                sb.AppendLine($"Key-Derivation: {argon2Params.Flavor}");
                sb.AppendLine($"Argon2-Memory: {argon2Params.Memory}");
                sb.AppendLine($"Argon2-Passes: {argon2Params.Passes}");
                sb.AppendLine($"Argon2-Parallelism: {argon2Params.Parallelism}");
                sb.AppendLine($"Argon2-Salt: {Convert.ToHexString(argon2Params.Salt).ToLowerInvariant()}");
            }
            else
            {
                // PPK v2 encryption
                var aesKey = DeriveKeyPpkV2(passphrase!, keyType);
                var iv = new byte[16]; // Zero IV for PPK v2
                macKey = DeriveMacKeyPpkV2(passphrase!);

                finalPrivateKeyBlob = EncryptPrivateKeyBlob(privateKeyBlob, aesKey, iv);
            }
        }
        else
        {
            finalPrivateKeyBlob = privateKeyBlob;

            // MAC key for unencrypted files
            if (version == 3)
            {
                macKey = new byte[32]; // 32-byte zero key for v3
            }
            else
            {
                macKey = DeriveMacKeyPpkV2(string.Empty);
            }
        }

        // Private key lines
        var privateKeyBase64 = Convert.ToBase64String(finalPrivateKeyBlob);
        var privateKeyLines = SplitIntoLines(privateKeyBase64, 64);
        sb.AppendLine($"Private-Lines: {privateKeyLines.Count}");
        foreach (var line in privateKeyLines)
        {
            sb.AppendLine(line);
        }

        // Compute MAC
        var mac = ComputePpkMac(
            version: version,
            keyType: keyType,
            encryption: encryption,
            comment: comment,
            publicKeyBlob: publicKeyBlob,
            privateKeyBlob: finalPrivateKeyBlob,
            macKey: macKey);

        sb.AppendLine($"Private-MAC: {mac}");

        return sb.ToString();
    }

    private byte[] EncryptPrivateKeyBlob(byte[] privateKeyBlob, byte[] aesKey, byte[] iv)
    {
        // Pad to AES block size (16 bytes) with PKCS#7-style padding
        var blockSize = 16;
        var paddingLength = blockSize - (privateKeyBlob.Length % blockSize);
        var paddedBlob = new byte[privateKeyBlob.Length + paddingLength];

        Array.Copy(privateKeyBlob, paddedBlob, privateKeyBlob.Length);

        // PKCS#7 padding: all padding bytes have value = padding length
        for (int i = privateKeyBlob.Length; i < paddedBlob.Length; i++)
        {
            paddedBlob[i] = (byte)paddingLength;
        }

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // We handle padding manually

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(paddedBlob, 0, paddedBlob.Length);
    }

    private string ComputePpkMac(
        int version,
        string keyType,
        string encryption,
        string comment,
        byte[] publicKeyBlob,
        byte[] privateKeyBlob,
        byte[] macKey)
    {
        // Build MAC data: key-type, encryption, comment, public-key, private-key
        using var macDataStream = new MemoryStream();
        using var macWriter = new BinaryWriter(macDataStream);

        // Write key type
        var keyTypeBytes = Encoding.UTF8.GetBytes(keyType);
        WriteUInt32(macWriter, (uint)keyTypeBytes.Length);
        macWriter.Write(keyTypeBytes);

        // Write encryption
        var encryptionBytes = Encoding.UTF8.GetBytes(encryption);
        WriteUInt32(macWriter, (uint)encryptionBytes.Length);
        macWriter.Write(encryptionBytes);

        // Write comment
        var commentBytes = Encoding.UTF8.GetBytes(comment);
        WriteUInt32(macWriter, (uint)commentBytes.Length);
        macWriter.Write(commentBytes);

        // Write public key blob
        WriteUInt32(macWriter, (uint)publicKeyBlob.Length);
        macWriter.Write(publicKeyBlob);

        // Write private key blob
        WriteUInt32(macWriter, (uint)privateKeyBlob.Length);
        macWriter.Write(privateKeyBlob);

        var macData = macDataStream.ToArray();

        // Compute MAC based on version
        byte[] mac;
        if (version == 3)
        {
            using var hmac = new HMACSHA256(macKey);
            mac = hmac.ComputeHash(macData);
        }
        else
        {
            using var hmac = new HMACSHA1(macKey);
            mac = hmac.ComputeHash(macData);
        }

        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    private List<string> SplitIntoLines(string base64, int lineLength)
    {
        var lines = new List<string>();
        for (int i = 0; i < base64.Length; i += lineLength)
        {
            lines.Add(base64.Substring(i, Math.Min(lineLength, base64.Length - i)));
        }
        return lines;
    }
}
