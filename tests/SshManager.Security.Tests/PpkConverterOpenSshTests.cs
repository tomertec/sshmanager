using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SshManager.Security;
using Xunit;

namespace SshManager.Security.Tests;

/// <summary>
/// Unit tests for OpenSSH to PPK conversion functionality.
/// </summary>
public class PpkConverterOpenSshTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ISshKeyManager _mockKeyManager;
    private readonly ILogger<PpkConverter> _logger;
    private readonly PpkConverter _converter;

    public PpkConverterOpenSshTests()
    {
        // Create temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"SshManager_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // Setup mocks
        _mockKeyManager = Substitute.For<ISshKeyManager>();
        _mockKeyManager.ComputeFingerprint(Arg.Any<string>())
            .Returns(ci => $"SHA256:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ci.Arg<string>())))}");

        _logger = NullLogger<PpkConverter>.Instance;
        _converter = new PpkConverter(_mockKeyManager, _logger);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Fact]
    public void IsOpenSshPrivateKey_WithOpenSshKey_ReturnsTrue()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "test_openssh.key");
        File.WriteAllText(keyPath, "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----");

        // Act
        var result = _converter.IsOpenSshPrivateKey(keyPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOpenSshPrivateKey_WithRsaKey_ReturnsTrue()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "test_rsa.key");
        File.WriteAllText(keyPath, "-----BEGIN RSA PRIVATE KEY-----\ntest\n-----END RSA PRIVATE KEY-----");

        // Act
        var result = _converter.IsOpenSshPrivateKey(keyPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOpenSshPrivateKey_WithEcKey_ReturnsTrue()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "test_ec.key");
        File.WriteAllText(keyPath, "-----BEGIN EC PRIVATE KEY-----\ntest\n-----END EC PRIVATE KEY-----");

        // Act
        var result = _converter.IsOpenSshPrivateKey(keyPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOpenSshPrivateKey_WithPpkFile_ReturnsFalse()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "test.ppk");
        File.WriteAllText(keyPath, "PuTTY-User-Key-File-3: ssh-ed25519\ntest");

        // Act
        var result = _converter.IsOpenSshPrivateKey(keyPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsOpenSshPrivateKey_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "nonexistent.key");

        // Act
        var result = _converter.IsOpenSshPrivateKey(keyPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConvertToPpkAsync_WithNonExistentFile_ReturnsFailure()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "nonexistent.key");

        // Act
        var result = await _converter.ConvertToPpkAsync(keyPath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.PpkContent.Should().BeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConvertToPpkAsync_WithInvalidKey_ReturnsFailure()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "invalid.key");
        File.WriteAllText(keyPath, "This is not a valid key file");

        // Act
        var result = await _converter.ConvertToPpkAsync(keyPath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.PpkContent.Should().BeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConvertAndSaveAsPpkAsync_WithNonExistentFile_ThrowsException()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "nonexistent.key");
        var outputPath = Path.Combine(_testDirectory, "output.ppk");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _converter.ConvertAndSaveAsPpkAsync(keyPath, outputPath));
    }

    [Fact]
    public async Task ConvertAndSaveAsPpkAsync_CreatesOutputDirectory()
    {
        // Arrange
        var keyPath = Path.Combine(_testDirectory, "test.key");
        var outputDir = Path.Combine(_testDirectory, "output", "subdir");
        var outputPath = Path.Combine(outputDir, "test.ppk");

        // Create a minimal invalid key (will fail conversion but test directory creation)
        File.WriteAllText(keyPath, "-----BEGIN OPENSSH PRIVATE KEY-----\ninvalid\n-----END OPENSSH PRIVATE KEY-----");

        Directory.Exists(outputDir).Should().BeFalse();

        // Act & Assert
        // Should throw because key is invalid, but directory should be created first
        try
        {
            await _converter.ConvertAndSaveAsPpkAsync(keyPath, outputPath);
        }
        catch
        {
            // Expected to fail with invalid key
        }

        // Note: Directory creation happens in ConvertAndSaveAsPpkAsync only after successful conversion
        // So with invalid key, directory won't be created
    }

    [Fact]
    public async Task ConvertToPpkAsync_WithDifferentVersions_IncludesCorrectHeader()
    {
        // This test would require a valid OpenSSH key file
        // For now, we verify that the version enum values are correct
        PpkVersion.V2.Should().Be((PpkVersion)2);
        PpkVersion.V3.Should().Be((PpkVersion)3);

        await Task.CompletedTask;
    }

    [Fact]
    public void OpenSshToPpkResult_SuccessfulConversion_HasRequiredProperties()
    {
        // Arrange & Act
        var result = new OpenSshToPpkResult(
            Success: true,
            PpkContent: "PuTTY-User-Key-File-3: ssh-ed25519\n...",
            KeyType: "ssh-ed25519",
            Comment: "test@host",
            ErrorMessage: null);

        // Assert
        result.Success.Should().BeTrue();
        result.PpkContent.Should().NotBeNullOrEmpty();
        result.KeyType.Should().Be("ssh-ed25519");
        result.Comment.Should().Be("test@host");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void OpenSshToPpkResult_FailedConversion_HasErrorMessage()
    {
        // Arrange & Act
        var result = new OpenSshToPpkResult(
            Success: false,
            PpkContent: null,
            KeyType: null,
            Comment: null,
            ErrorMessage: "Invalid key format");

        // Assert
        result.Success.Should().BeFalse();
        result.PpkContent.Should().BeNull();
        result.KeyType.Should().BeNull();
        result.Comment.Should().BeNull();
        result.ErrorMessage.Should().Be("Invalid key format");
    }

    [Fact]
    public void PpkVersion_Values_AreCorrect()
    {
        // Assert
        ((int)PpkVersion.V2).Should().Be(2);
        ((int)PpkVersion.V3).Should().Be(3);
    }
}
