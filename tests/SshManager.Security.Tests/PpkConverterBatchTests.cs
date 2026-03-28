using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SshManager.Security;
using Xunit;

namespace SshManager.Security.Tests;

/// <summary>
/// Unit tests for batch PPK conversion functionality.
/// </summary>
public class PpkConverterBatchTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ISshKeyManager _mockKeyManager;
    private readonly ILogger<PpkConverter> _logger;

    public PpkConverterBatchTests()
    {
        // Create temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"SshManager_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // Setup mocks
        _mockKeyManager = Substitute.For<ISshKeyManager>();
        _mockKeyManager.ComputeFingerprint(Arg.Any<string>())
            .Returns(ci => $"SHA256:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ci.Arg<string>())))}");

        _logger = NullLogger<PpkConverter>.Instance;
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
    public async Task ConvertBatchToOpenSshAsync_EmptyList_ReturnsEmptyResult()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var emptyList = Array.Empty<(string, string?)>();

        // Act
        var result = await converter.ConvertBatchToOpenSshAsync(emptyList);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task ConvertBatchToOpenSshAsync_WithNonExistentFiles_ReturnsFailureResults()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var files = new[]
        {
            (Path.Combine(_testDirectory, "nonexistent1.ppk"), (string?)null),
            (Path.Combine(_testDirectory, "nonexistent2.ppk"), "password")
        };

        // Act
        var result = await converter.ConvertBatchToOpenSshAsync(files);

        // Assert
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(2);
        result.Results.Should().HaveCount(2);

        foreach (var item in result.Results)
        {
            item.Success.Should().BeFalse();
            item.ErrorMessage.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task ConvertBatchToOpenSshAsync_WithInvalidPpkFiles_ReturnsFailureResults()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);

        // Create invalid PPK files (just plain text)
        var file1 = Path.Combine(_testDirectory, "invalid1.ppk");
        var file2 = Path.Combine(_testDirectory, "invalid2.ppk");
        await File.WriteAllTextAsync(file1, "This is not a PPK file");
        await File.WriteAllTextAsync(file2, "Neither is this");

        var files = new[]
        {
            (file1, (string?)null),
            (file2, (string?)null)
        };

        // Act
        var result = await converter.ConvertBatchToOpenSshAsync(files);

        // Assert
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(2);
        result.Results.Should().HaveCount(2);

        foreach (var item in result.Results)
        {
            item.Success.Should().BeFalse();
            item.ErrorMessage.Should().NotBeNullOrEmpty();
            item.ErrorMessage.Should().Contain("PPK");
        }
    }

    [Fact]
    public async Task ConvertBatchToOpenSshAsync_WithMixedResults_ReturnsMixedBatchResult()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);

        // Create one valid-looking header (will still fail during parsing but differently)
        var validHeaderFile = Path.Combine(_testDirectory, "validheader.ppk");
        await File.WriteAllTextAsync(validHeaderFile, "PuTTY-User-Key-File-2: ssh-rsa\nEncryption: none\n");

        // Create one invalid file
        var invalidFile = Path.Combine(_testDirectory, "invalid.ppk");
        await File.WriteAllTextAsync(invalidFile, "Invalid content");

        // Create one non-existent file reference
        var nonExistentFile = Path.Combine(_testDirectory, "doesnotexist.ppk");

        var files = new[]
        {
            (validHeaderFile, (string?)null),
            (invalidFile, (string?)null),
            (nonExistentFile, (string?)null)
        };

        // Act
        var result = await converter.ConvertBatchToOpenSshAsync(files);

        // Assert
        result.TotalCount.Should().Be(3);
        result.SuccessCount.Should().Be(0); // All should fail (incomplete PPK data)
        result.FailureCount.Should().Be(3);
        result.Results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ConvertBatchToOpenSshAsync_RespectsCancellation()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var cts = new CancellationTokenSource();

        // Create a large batch
        var files = Enumerable.Range(1, 100)
            .Select(i => (Path.Combine(_testDirectory, $"file{i}.ppk"), (string?)null))
            .ToArray();

        // Cancel immediately
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await converter.ConvertBatchToOpenSshAsync(files, cts.Token));
    }

    [Fact]
    public async Task ConvertBatchAndSaveAsync_EmptyList_ReturnsEmptyResult()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var outputDir = Path.Combine(_testDirectory, "output");
        var emptyList = Array.Empty<(string, string?)>();

        // Act
        var result = await converter.ConvertBatchAndSaveAsync(emptyList, outputDir);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(0);
        result.Results.Should().BeEmpty();
        Directory.Exists(outputDir).Should().BeTrue(); // Directory should be created
    }

    [Fact]
    public async Task ConvertBatchAndSaveAsync_CreatesOutputDirectory()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var outputDir = Path.Combine(_testDirectory, "newoutput");
        var files = Array.Empty<(string, string?)>();

        Directory.Exists(outputDir).Should().BeFalse();

        // Act
        await converter.ConvertBatchAndSaveAsync(files, outputDir);

        // Assert
        Directory.Exists(outputDir).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertBatchAndSaveAsync_WithNonExistentFiles_ReturnsFailureResults()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var outputDir = Path.Combine(_testDirectory, "output");
        var files = new[]
        {
            (Path.Combine(_testDirectory, "missing1.ppk"), (string?)null),
            (Path.Combine(_testDirectory, "missing2.ppk"), "password")
        };

        // Act
        var result = await converter.ConvertBatchAndSaveAsync(files, outputDir);

        // Assert
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(2);
        result.Results.Should().HaveCount(2);

        foreach (var item in result.Results)
        {
            item.Success.Should().BeFalse();
            item.SavedPath.Should().BeNull();
            item.ErrorMessage.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task ConvertBatchAndSaveAsync_RespectsCancellation()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var outputDir = Path.Combine(_testDirectory, "output");
        var cts = new CancellationTokenSource();

        var files = Enumerable.Range(1, 100)
            .Select(i => (Path.Combine(_testDirectory, $"file{i}.ppk"), (string?)null))
            .ToArray();

        // Cancel immediately
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await converter.ConvertBatchAndSaveAsync(files, outputDir, cts.Token));
    }

    [Fact]
    public async Task ConvertBatchAndSaveAsync_EachFailureIsIndependent()
    {
        // Arrange
        var converter = new PpkConverter(_mockKeyManager, _logger);
        var outputDir = Path.Combine(_testDirectory, "output");

        // Mix of files: some exist, some don't
        var existingInvalidFile = Path.Combine(_testDirectory, "invalid.ppk");
        await File.WriteAllTextAsync(existingInvalidFile, "Invalid PPK content");

        var files = new[]
        {
            (Path.Combine(_testDirectory, "missing1.ppk"), (string?)null),  // Missing
            (existingInvalidFile, (string?)null),                            // Exists but invalid
            (Path.Combine(_testDirectory, "missing2.ppk"), "pass")           // Missing
        };

        // Act
        var result = await converter.ConvertBatchAndSaveAsync(files, outputDir);

        // Assert
        result.TotalCount.Should().Be(3);
        result.FailureCount.Should().Be(3); // All should fail independently
        result.Results.Should().HaveCount(3);

        // Each result should have its own error message
        result.Results.Should().AllSatisfy(r =>
        {
            r.Success.Should().BeFalse();
            r.ErrorMessage.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void BatchPpkConversionResult_PropertiesMatchCounts()
    {
        // Arrange
        var results = new List<PpkBatchItemResult>
        {
            new("file1.ppk", true, null, "output1", null),
            new("file2.ppk", true, null, "output2", null),
            new("file3.ppk", false, null, null, "Error")
        };

        // Act
        var batchResult = new BatchPpkConversionResult(
            TotalCount: 3,
            SuccessCount: 2,
            FailureCount: 1,
            Results: results);

        // Assert
        batchResult.TotalCount.Should().Be(3);
        batchResult.SuccessCount.Should().Be(2);
        batchResult.FailureCount.Should().Be(1);
        batchResult.Results.Should().HaveCount(3);
        batchResult.Results.Count(r => r.Success).Should().Be(2);
        batchResult.Results.Count(r => !r.Success).Should().Be(1);
    }

    [Fact]
    public void PpkBatchItemResult_SuccessfulConversion_HasRequiredProperties()
    {
        // Arrange
        var conversionResult = new PpkConversionResult(
            Success: true,
            OpenSshPrivateKey: "-----BEGIN OPENSSH PRIVATE KEY-----\n...",
            OpenSshPublicKey: "ssh-rsa AAAA... comment",
            Fingerprint: "SHA256:abc123",
            KeyType: "ssh-rsa",
            Comment: "test key",
            ErrorMessage: null);

        // Act
        var itemResult = new PpkBatchItemResult(
            PpkPath: @"C:\test\key.ppk",
            Success: true,
            ConversionResult: conversionResult,
            SavedPath: @"C:\output\key",
            ErrorMessage: null);

        // Assert
        itemResult.Success.Should().BeTrue();
        itemResult.PpkPath.Should().Be(@"C:\test\key.ppk");
        itemResult.ConversionResult.Should().NotBeNull();
        itemResult.ConversionResult!.Success.Should().BeTrue();
        itemResult.SavedPath.Should().Be(@"C:\output\key");
        itemResult.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void PpkBatchItemResult_FailedConversion_HasErrorMessage()
    {
        // Act
        var itemResult = new PpkBatchItemResult(
            PpkPath: @"C:\test\invalid.ppk",
            Success: false,
            ConversionResult: null,
            SavedPath: null,
            ErrorMessage: "Invalid PPK format");

        // Assert
        itemResult.Success.Should().BeFalse();
        itemResult.PpkPath.Should().Be(@"C:\test\invalid.ppk");
        itemResult.ConversionResult.Should().BeNull();
        itemResult.SavedPath.Should().BeNull();
        itemResult.ErrorMessage.Should().Be("Invalid PPK format");
    }
}
