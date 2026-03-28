using FluentAssertions;
using Renci.SshNet;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Tests.Services;

/// <summary>
/// Unit tests for AlgorithmConfigurator static utility class.
/// Tests algorithm configuration applied to ConnectionInfo.
/// </summary>
/// <remarks>
/// Note: The ReorderAlgorithms method is internal. These tests verify the public
/// ConfigureAlgorithms method's behavior through its effects on ConnectionInfo.
/// </remarks>
public class AlgorithmConfiguratorTests
{
    [Fact]
    public void ConfigureAlgorithms_WithValidConnectionInfo_DoesNotThrow()
    {
        // Arrange
        var connInfo = new PasswordConnectionInfo("test.example.com", "user", "password");

        // Act
        var act = () => AlgorithmConfigurator.ConfigureAlgorithms(connInfo);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureAlgorithms_WithLogger_DoesNotThrow()
    {
        // Arrange
        var connInfo = new PasswordConnectionInfo("test.example.com", "user", "password");
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        // Act
        var act = () => AlgorithmConfigurator.ConfigureAlgorithms(connInfo, logger);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureAlgorithms_PreservesAllKeyExchangeAlgorithms()
    {
        // Arrange
        var connInfo = new PasswordConnectionInfo("test.example.com", "user", "password");
        var originalCount = connInfo.KeyExchangeAlgorithms.Count;

        // Act
        AlgorithmConfigurator.ConfigureAlgorithms(connInfo);

        // Assert - No algorithms should be removed, only reordered
        connInfo.KeyExchangeAlgorithms.Should().HaveCount(originalCount,
            "all original algorithms should be preserved");
    }

    [Fact]
    public void ConfigureAlgorithms_PrioritizesCurve25519_WhenAvailable()
    {
        // Arrange
        var connInfo = new PasswordConnectionInfo("test.example.com", "user", "password");

        // Act
        AlgorithmConfigurator.ConfigureAlgorithms(connInfo);

        // Assert
        var algorithms = connInfo.KeyExchangeAlgorithms.Keys.ToList();

        // curve25519-sha256 or curve25519-sha256@libssh.org should be in first few positions
        // if supported by SSH.NET
        var curve25519Index = algorithms.FindIndex(a =>
            a.Contains("curve25519", StringComparison.OrdinalIgnoreCase));

        if (curve25519Index >= 0)
        {
            curve25519Index.Should().BeLessThan(5,
                "curve25519 should be prioritized in first few positions when available");
        }
    }

    [Fact]
    public void ConfigureAlgorithms_ContainsExpectedAlgorithms()
    {
        // Arrange
        var connInfo = new PasswordConnectionInfo("test.example.com", "user", "password");

        // Act
        AlgorithmConfigurator.ConfigureAlgorithms(connInfo);

        // Assert - SSH.NET should provide these basic algorithms
        connInfo.KeyExchangeAlgorithms.Should().NotBeEmpty();
        connInfo.Encryptions.Should().NotBeEmpty();
        connInfo.HmacAlgorithms.Should().NotBeEmpty();
    }

    [Fact]
    public void ConfigureAlgorithms_MultipleCallsAreIdempotent()
    {
        // Arrange
        var connInfo = new PasswordConnectionInfo("test.example.com", "user", "password");

        // Act - Call multiple times
        AlgorithmConfigurator.ConfigureAlgorithms(connInfo);
        var firstCallAlgorithms = connInfo.KeyExchangeAlgorithms.Keys.ToList();

        AlgorithmConfigurator.ConfigureAlgorithms(connInfo);
        var secondCallAlgorithms = connInfo.KeyExchangeAlgorithms.Keys.ToList();

        // Assert - Order should be the same after multiple calls
        firstCallAlgorithms.Should().Equal(secondCallAlgorithms,
            "multiple calls should produce the same order");
    }
}
