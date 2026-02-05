using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace SshManager.Security;

/// <summary>
/// Service for managing SSH keys using SSH.NET and .NET cryptography.
/// </summary>
public sealed partial class SshKeyManagerService : ISshKeyManager
{
    private readonly ILogger<SshKeyManagerService> _logger;

    public SshKeyManagerService(ILogger<SshKeyManagerService>? logger = null)
    {
        _logger = logger ?? NullLogger<SshKeyManagerService>.Instance;
    }

    public string GetDefaultSshDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh");
    }

    public async Task<SshKeyPair> GenerateKeyAsync(
        SshKeyType keyType,
        string? passphrase = null,
        string? comment = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Generating {KeyType} key", keyType);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            return keyType switch
            {
                SshKeyType.Rsa2048 => GenerateRsaKey(2048, passphrase, comment),
                SshKeyType.Rsa4096 => GenerateRsaKey(4096, passphrase, comment),
                SshKeyType.Ed25519 => GenerateEd25519Key(passphrase, comment),
                SshKeyType.Ecdsa256 => GenerateEcdsaKey(256, passphrase, comment),
                SshKeyType.Ecdsa384 => GenerateEcdsaKey(384, passphrase, comment),
                SshKeyType.Ecdsa521 => GenerateEcdsaKey(521, passphrase, comment),
                _ => throw new ArgumentOutOfRangeException(nameof(keyType))
            };
        }, ct);
    }

    private SshKeyPair GenerateRsaKey(int keySize, string? passphrase, string? comment)
    {
        using var rsa = RSA.Create(keySize);

        var privateKeyPem = ExportRsaPrivateKey(rsa, passphrase);
        var publicKey = FormatRsaPublicKey(rsa, comment);
        var fingerprint = ComputeFingerprint(publicKey);

        _logger.LogDebug("Generated RSA-{KeySize} key with fingerprint {Fingerprint}", keySize, fingerprint);

        return new SshKeyPair
        {
            KeyType = keySize == 2048 ? SshKeyType.Rsa2048 : SshKeyType.Rsa4096,
            PrivateKey = privateKeyPem,
            PublicKey = publicKey,
            Fingerprint = fingerprint,
            Comment = comment,
            IsEncrypted = !string.IsNullOrEmpty(passphrase),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private SshKeyPair GenerateEd25519Key(string? passphrase, string? comment)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "SshManager",
            "keygen",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDir);

        var privateKeyPath = Path.Combine(tempDir, "id_ed25519");

        try
        {
            RunSshKeygen(privateKeyPath, passphrase, comment);

            var privateKey = File.ReadAllText(privateKeyPath);
            var publicKey = File.ReadAllText(privateKeyPath + ".pub").Trim();
            var fingerprint = ComputeFingerprint(publicKey);

            _logger.LogDebug("Generated Ed25519 key with fingerprint {Fingerprint}", fingerprint);

            return new SshKeyPair
            {
                KeyType = SshKeyType.Ed25519,
                PrivateKey = privateKey,
                PublicKey = publicKey,
                Fingerprint = fingerprint,
                Comment = comment,
                IsEncrypted = !string.IsNullOrEmpty(passphrase),
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temporary Ed25519 key directory");
            }
        }
    }

    private static void RunSshKeygen(string privateKeyPath, string? passphrase, string? comment)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ssh-keygen",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add("ed25519");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(privateKeyPath);
            startInfo.ArgumentList.Add("-q");

            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(comment) ? string.Empty : comment);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start ssh-keygen process.");

            var input = passphrase ?? string.Empty;
            process.StandardInput.WriteLine(input);
            process.StandardInput.WriteLine(input);
            process.StandardInput.Close();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException($"ssh-keygen failed: {message.Trim()}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "ssh-keygen was not found. Install the OpenSSH client feature to enable Ed25519 key generation.",
                ex);
        }
    }

    private SshKeyPair GenerateEcdsaKey(int keySize, string? passphrase, string? comment)
    {
        var curve = keySize switch
        {
            256 => ECCurve.NamedCurves.nistP256,
            384 => ECCurve.NamedCurves.nistP384,
            521 => ECCurve.NamedCurves.nistP521,
            _ => throw new ArgumentOutOfRangeException(nameof(keySize))
        };

        using var ecdsa = ECDsa.Create(curve);

        var privateKeyPem = ExportEcdsaPrivateKey(ecdsa, passphrase);
        var publicKey = FormatEcdsaPublicKey(ecdsa, keySize, comment);
        var fingerprint = ComputeFingerprint(publicKey);

        var keyType = keySize switch
        {
            256 => SshKeyType.Ecdsa256,
            384 => SshKeyType.Ecdsa384,
            521 => SshKeyType.Ecdsa521,
            _ => throw new ArgumentOutOfRangeException(nameof(keySize))
        };

        _logger.LogDebug("Generated ECDSA-{KeySize} key with fingerprint {Fingerprint}", keySize, fingerprint);

        return new SshKeyPair
        {
            KeyType = keyType,
            PrivateKey = privateKeyPem,
            PublicKey = publicKey,
            Fingerprint = fingerprint,
            Comment = comment,
            IsEncrypted = !string.IsNullOrEmpty(passphrase),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<SshKeyInfo>> GetExistingKeysAsync(CancellationToken ct = default)
    {
        var sshDir = GetDefaultSshDirectory();
        var keys = new List<SshKeyInfo>();

        if (!Directory.Exists(sshDir))
        {
            _logger.LogDebug("SSH directory does not exist: {SshDir}", sshDir);
            return keys;
        }

        await Task.Run(() =>
        {
            // Look for private key files (those without .pub extension)
            var files = Directory.GetFiles(sshDir)
                .Where(f => !f.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.EndsWith("known_hosts", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.EndsWith("authorized_keys", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var keyInfo = ParseKeyFile(file);
                    if (keyInfo != null)
                    {
                        keys.Add(keyInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse key file: {File}", file);
                }
            }
        }, ct);

        _logger.LogInformation("Found {Count} SSH keys in {SshDir}", keys.Count, sshDir);
        return keys;
    }

    private SshKeyInfo? ParseKeyFile(string privateKeyPath)
    {
        var content = File.ReadAllText(privateKeyPath);

        // Check if it looks like a private key
        if (!content.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var publicKeyPath = privateKeyPath + ".pub";
        string? publicKeyContent = null;
        string? comment = null;
        string? fingerprint = null;

        if (File.Exists(publicKeyPath))
        {
            publicKeyContent = File.ReadAllText(publicKeyPath).Trim();
            var parts = publicKeyContent.Split(' ', 3);
            if (parts.Length >= 3)
            {
                comment = parts[2];
            }
            fingerprint = ComputeFingerprint(publicKeyContent);
        }

        var fileInfo = new FileInfo(privateKeyPath);
        var isEncrypted = content.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase);

        // Determine key type
        var (keyType, keyTypeString, keySize) = DetermineKeyType(content, publicKeyContent);

        return new SshKeyInfo
        {
            PrivateKeyPath = privateKeyPath,
            PublicKeyPath = File.Exists(publicKeyPath) ? publicKeyPath : null,
            KeyType = keyType,
            KeyTypeString = keyTypeString,
            KeySize = keySize,
            Fingerprint = fingerprint,
            Comment = comment,
            IsEncrypted = isEncrypted,
            CreatedAt = fileInfo.CreationTimeUtc,
            ModifiedAt = fileInfo.LastWriteTimeUtc
        };
    }

    private static (SshKeyType? keyType, string keyTypeString, int? keySize) DetermineKeyType(
        string privateKeyContent,
        string? publicKeyContent)
    {
        // Try to determine from public key first
        if (!string.IsNullOrEmpty(publicKeyContent))
        {
            if (publicKeyContent.StartsWith("ssh-ed25519"))
                return (SshKeyType.Ed25519, "Ed25519", null);

            if (publicKeyContent.StartsWith("ecdsa-sha2-nistp256"))
                return (SshKeyType.Ecdsa256, "ECDSA", 256);

            if (publicKeyContent.StartsWith("ecdsa-sha2-nistp384"))
                return (SshKeyType.Ecdsa384, "ECDSA", 384);

            if (publicKeyContent.StartsWith("ecdsa-sha2-nistp521"))
                return (SshKeyType.Ecdsa521, "ECDSA", 521);

            if (publicKeyContent.StartsWith("ssh-rsa"))
            {
                // Try to determine RSA key size from the public key
                try
                {
                    var parts = publicKeyContent.Split(' ');
                    if (parts.Length >= 2)
                    {
                        var keyData = Convert.FromBase64String(parts[1]);
                        // RSA key size can be estimated from the key data length
                        var estimatedBits = (keyData.Length - 20) * 8; // Rough estimate
                        if (estimatedBits >= 3500)
                            return (SshKeyType.Rsa4096, "RSA", 4096);
                        return (SshKeyType.Rsa2048, "RSA", 2048);
                    }
                }
                catch
                {
                    // Fall through to default RSA
                }
                return (SshKeyType.Rsa2048, "RSA", 2048);
            }
        }

        // Fall back to checking private key content
        if (privateKeyContent.Contains("OPENSSH PRIVATE KEY"))
        {
            // Modern OpenSSH format - could be any type
            if (privateKeyContent.Contains("ed25519", StringComparison.OrdinalIgnoreCase))
                return (SshKeyType.Ed25519, "Ed25519", null);
            return (null, "OpenSSH", null);
        }

        if (privateKeyContent.Contains("RSA PRIVATE KEY"))
            return (SshKeyType.Rsa2048, "RSA", null);

        if (privateKeyContent.Contains("EC PRIVATE KEY"))
            return (SshKeyType.Ecdsa256, "ECDSA", null);

        if (privateKeyContent.Contains("DSA PRIVATE KEY"))
            return (null, "DSA (deprecated)", null);

        return (null, "Unknown", null);
    }

    public async Task<string> GetPublicKeyAsync(
        string privateKeyPath,
        string? passphrase = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // First check if public key file exists
            var publicKeyPath = privateKeyPath + ".pub";
            if (File.Exists(publicKeyPath))
            {
                return File.ReadAllText(publicKeyPath).Trim();
            }

            // SSH.NET's PrivateKeyFile doesn't easily expose the public key directly
            // So we just validate the key loads and return a message indicating
            // the public key file should be used
            try
            {
                using var keyFile = string.IsNullOrEmpty(passphrase)
                    ? new PrivateKeyFile(privateKeyPath)
                    : new PrivateKeyFile(privateKeyPath, passphrase);

                // Key loaded successfully, but we can't easily get the public key format
                // User should look at the .pub file
                return $"[Public key for {Path.GetFileName(privateKeyPath)}]";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not load private key: {ex.Message}", ex);
            }
        }, ct);
    }

    public async Task SaveKeyPairAsync(SshKeyPair keyPair, string privateKeyPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(privateKeyPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save private key
            File.WriteAllText(privateKeyPath, keyPair.PrivateKey);

            // Set restrictive file permissions on Windows (owner-only access)
            try
            {
                var fileInfo = new FileInfo(privateKeyPath);
                var security = fileInfo.GetAccessControl();

                // Disable inheritance and remove all existing rules
                security.SetAccessRuleProtection(true, false);

                // Get current user identity
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();

                // Add full control for current user only
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    currentUser.Name,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));

                fileInfo.SetAccessControl(security);

                _logger.LogDebug("Set restrictive file permissions on private key: {Path}", privateKeyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not set file permissions on private key");
            }

            // Save public key
            var publicKeyPath = privateKeyPath + ".pub";
            File.WriteAllText(publicKeyPath, keyPair.PublicKey);

            _logger.LogInformation("Saved key pair to {PrivateKeyPath}", privateKeyPath);
        }, ct);
    }

    public async Task<bool> ValidateKeyAsync(
        string privateKeyPath,
        string? passphrase = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(privateKeyPath))
                {
                    return false;
                }

                using var keyFile = string.IsNullOrEmpty(passphrase)
                    ? new PrivateKeyFile(privateKeyPath)
                    : new PrivateKeyFile(privateKeyPath, passphrase);

                // If we got here, the key is valid
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Key validation failed for {Path}", privateKeyPath);
                return false;
            }
        }, ct);
    }

    public async Task DeleteKeyAsync(string privateKeyPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(privateKeyPath))
            {
                File.Delete(privateKeyPath);
                _logger.LogInformation("Deleted private key: {Path}", privateKeyPath);
            }

            var publicKeyPath = privateKeyPath + ".pub";
            if (File.Exists(publicKeyPath))
            {
                File.Delete(publicKeyPath);
                _logger.LogInformation("Deleted public key: {Path}", publicKeyPath);
            }
        }, ct);
    }

    public string ComputeFingerprint(string publicKey)
    {
        try
        {
            var parts = publicKey.Trim().Split(' ');
            if (parts.Length < 2)
            {
                return "Invalid key format";
            }

            var keyData = Convert.FromBase64String(parts[1]);
            var hash = SHA256.HashData(keyData);
            var base64Hash = Convert.ToBase64String(hash).TrimEnd('=');

            return $"SHA256:{base64Hash}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute fingerprint");
            return "Unknown";
        }
    }

    #region Key Export Helpers

    private static string ExportRsaPrivateKey(RSA rsa, string? passphrase)
    {
        return CryptoExportHelper.ExportRsaPrivateKey(rsa, passphrase);
    }

    private static string ExportEcdsaPrivateKey(ECDsa ecdsa, string? passphrase)
    {
        return CryptoExportHelper.ExportEcdsaPrivateKey(ecdsa, passphrase);
    }

    private static string FormatPem(byte[] data, string label)
    {
        return CryptoExportHelper.FormatPem(data, label);
    }

    private static string FormatRsaPublicKey(RSA rsa, string? comment)
    {
        var parameters = rsa.ExportParameters(false);

        // SSH RSA public key format: ssh-rsa <base64 data> [comment]
        // The data is: length(4) + "ssh-rsa" + length(4) + e + length(4) + n
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        WriteString(writer, "ssh-rsa");
        WriteMpint(writer, parameters.Exponent!);
        WriteMpint(writer, parameters.Modulus!);

        var keyData = Convert.ToBase64String(ms.ToArray());
        return string.IsNullOrEmpty(comment)
            ? $"ssh-rsa {keyData}"
            : $"ssh-rsa {keyData} {comment}";
    }

    private static string FormatEcdsaPublicKey(ECDsa ecdsa, int keySize, string? comment)
    {
        var parameters = ecdsa.ExportParameters(false);

        var curveName = keySize switch
        {
            256 => "nistp256",
            384 => "nistp384",
            521 => "nistp521",
            _ => throw new ArgumentOutOfRangeException(nameof(keySize))
        };

        var keyType = $"ecdsa-sha2-{curveName}";

        // SSH ECDSA public key format
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        WriteString(writer, keyType);
        WriteString(writer, curveName);

        // Write Q (public key point) as uncompressed point: 0x04 || X || Y
        var qLength = 1 + parameters.Q.X!.Length + parameters.Q.Y!.Length;
        writer.Write(ToBigEndian(qLength));
        writer.Write((byte)0x04);
        writer.Write(parameters.Q.X);
        writer.Write(parameters.Q.Y);

        var keyData = Convert.ToBase64String(ms.ToArray());
        return string.IsNullOrEmpty(comment)
            ? $"{keyType} {keyData}"
            : $"{keyType} {keyData} {comment}";
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(ToBigEndian(bytes.Length));
        writer.Write(bytes);
    }

    private static void WriteMpint(BinaryWriter writer, byte[] value)
    {
        // SSH mpint format: if the high bit is set, prepend a zero byte
        if (value.Length > 0 && (value[0] & 0x80) != 0)
        {
            writer.Write(ToBigEndian(value.Length + 1));
            writer.Write((byte)0);
            writer.Write(value);
        }
        else
        {
            writer.Write(ToBigEndian(value.Length));
            writer.Write(value);
        }
    }

    private static byte[] ToBigEndian(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return bytes;
    }

    #endregion

    public async Task<SshKeyPair?> ImportPpkAsync(
        string ppkPath,
        string? passphrase = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Importing PPK file: {Path}", ppkPath);

        // Create a PpkConverter instance (avoiding circular dependency by not using DI here)
        // Note: passing null for logger as we can't convert ILogger<T> types
        var converter = new PpkConverter(this, logger: null);

        var result = await converter.ConvertToOpenSshAsync(ppkPath, passphrase, ct);

        if (!result.Success || result.OpenSshPrivateKey == null || result.OpenSshPublicKey == null)
        {
            _logger.LogWarning("Failed to import PPK file: {Error}", result.ErrorMessage);
            return null;
        }

        // Determine SSH key type from the key type string
        var keyType = DetermineSshKeyType(result.KeyType ?? "");

        _logger.LogInformation("Successfully imported PPK file as {KeyType}", keyType);

        return new SshKeyPair
        {
            KeyType = keyType,
            PrivateKey = result.OpenSshPrivateKey,
            PublicKey = result.OpenSshPublicKey,
            Fingerprint = result.Fingerprint ?? "",
            Comment = result.Comment,
            IsEncrypted = false, // Converted keys are unencrypted
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static SshKeyType DetermineSshKeyType(string keyTypeString)
    {
        return keyTypeString switch
        {
            "ssh-rsa" => SshKeyType.Rsa2048, // Default to 2048, actual size may vary
            "ssh-ed25519" => SshKeyType.Ed25519,
            "ecdsa-sha2-nistp256" => SshKeyType.Ecdsa256,
            "ecdsa-sha2-nistp384" => SshKeyType.Ecdsa384,
            "ecdsa-sha2-nistp521" => SshKeyType.Ecdsa521,
            _ => SshKeyType.Rsa2048 // Default fallback
        };
    }
}
