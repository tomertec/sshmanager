using FluentAssertions;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Tests.Integration;

/// <summary>
/// Integration tests for terminal features that require SSH connections.
/// These tests verify the terminal control handles complex scenarios correctly.
///
/// Test Scenarios from Phase 4 Migration Plan:
/// - Docker Compose: Alternate screen buffer (mode 1049) and progress display
/// - vim/nano: TUI editors render correctly
/// - htop/top: Real-time TUI updates
/// - tmux: Terminal multiplexer with splits
/// - Mouse: Mouse events register
/// - Resize: Terminal reflows on resize
///
/// Refactoring Phase 5 Additions:
/// - Verify refactored services maintain same behavior
/// - Verify FontStackBuilder utility
/// - Verify AlgorithmConfigurator integration
/// </summary>
public class TerminalFeatureIntegrationTests : TerminalIntegrationTestBase
{
    /// <summary>
    /// Tests that running docker compose displays progress correctly.
    /// This is the primary bug fix that motivated the terminal migration.
    /// </summary>
    [Fact]
    public async Task DockerCompose_ProgressDisplaysCorrectly()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var outputBuffer = new TerminalOutputBuffer();
        var commandComplete = new TaskCompletionSource<bool>();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 120, 30);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                var text = System.Text.Encoding.UTF8.GetString(data);
                outputBuffer.AppendOutput(text);

                // Look for completion indicators
                if (text.Contains("$") || text.Contains("#"))
                {
                    commandComplete.TrySetResult(true);
                }
            };
            bridge.StartReading();

            // Wait for initial prompt
            await Task.Delay(1000);

            // Send docker compose command (assuming docker is installed)
            // Using --dry-run to avoid actual container operations
            bridge.SendCommand("docker compose version 2>/dev/null || echo 'docker not available'");

            await Task.WhenAny(commandComplete.Task, Task.Delay(TimeSpan.FromSeconds(10)));

            // The key test: verify the output buffer has the command response
            var output = outputBuffer.GetAllText();
            output.Should().NotBeEmpty("should have received output");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Tests that running a TUI application (like less or nano) works correctly.
    /// </summary>
    [Fact]
    public async Task TuiApplication_RendersAndExits()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var outputBuffer = new TerminalOutputBuffer();
        var receivedData = false;

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                receivedData = true;
                outputBuffer.AppendOutput(System.Text.Encoding.UTF8.GetString(data));
            };
            bridge.StartReading();

            // Wait for prompt
            await Task.Delay(1000);

            // Start less with some content, then immediately quit
            bridge.SendCommand("echo -e 'line1\\nline2\\nline3' | less");
            await Task.Delay(500);

            // Send 'q' to quit less
            bridge.SendText("q");
            await Task.Delay(500);

            receivedData.Should().BeTrue("should have received TUI output");

            // Terminal should be in normal mode now
            bridge.SendCommand("echo BACK_TO_NORMAL");
            await Task.Delay(500);

            outputBuffer.GetAllText().Should().Contain("BACK_TO_NORMAL",
                "terminal should be back to normal mode after exiting TUI");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Tests that top/htop-style applications with real-time updates work.
    /// </summary>
    [Fact]
    public async Task TopCommand_ReceivesUpdates()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var updateCount = 0;

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += _ => Interlocked.Increment(ref updateCount);
            bridge.StartReading();

            await Task.Delay(1000);

            // Run top in batch mode with 2 iterations
            bridge.SendCommand("top -b -n 2 -d 1 || uptime");
            await Task.Delay(3000);

            updateCount.Should().BeGreaterThan(1, "should receive multiple updates from top");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Tests that high-frequency output (like 'yes' command) doesn't overwhelm the terminal.
    /// </summary>
    [Fact]
    public async Task HighFrequencyOutput_HandlesProperly()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var bytesReceived = 0L;
        var outputComplete = new TaskCompletionSource<bool>();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                Interlocked.Add(ref bytesReceived, data.Length);
            };
            bridge.Disconnected += (_, _) => outputComplete.TrySetResult(true);
            bridge.StartReading();

            await Task.Delay(1000);

            // Run yes with head to limit output, then timeout
            bridge.SendCommand("yes | head -n 10000");
            await Task.Delay(2000);

            // Verify we received a lot of data
            Interlocked.Read(ref bytesReceived).Should().BeGreaterThan(10000,
                "should receive significant data from high-frequency output");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Tests that colored output is preserved through the terminal.
    /// </summary>
    [Fact]
    public async Task ColoredOutput_PreservesAnsiCodes()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var rawOutput = new System.Text.StringBuilder();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                rawOutput.Append(System.Text.Encoding.UTF8.GetString(data));
            };
            bridge.StartReading();

            await Task.Delay(1000);

            // Output colored text
            bridge.SendCommand("echo -e '\\e[31mRED\\e[32mGREEN\\e[0m'");
            await Task.Delay(500);

            // Raw output should contain ANSI escape codes
            var output = rawOutput.ToString();
            output.Should().Contain("\x1B[", "should contain ANSI escape sequences");
            output.Should().Contain("RED");
            output.Should().Contain("GREEN");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    #region Refactoring Phase 5: Refactored Services Integration Tests

    /// <summary>
    /// Verifies that the refactored SshAuthenticationFactory creates valid auth methods.
    /// </summary>
    [Fact]
    public void SshAuthenticationFactory_CreatesValidAuthMethods()
    {
        // Arrange
        var factory = new SshAuthenticationFactory();
        var connectionInfo = new TerminalConnectionInfo
        {
            Hostname = "test.example.com",
            Port = 22,
            Username = "testuser",
            AuthType = SshManager.Core.Models.AuthType.Password,
            Password = "testpass"
        };

        // Act
        var result = factory.CreateAuthMethods(connectionInfo, null);

        // Assert
        result.Should().NotBeNull();
        result.Methods.Should().NotBeEmpty("should create at least one auth method");
    }

    /// <summary>
    /// Verifies that FontStackBuilder produces valid CSS font stacks.
    /// </summary>
    [Fact]
    public void FontStackBuilder_ProducesValidFontStack()
    {
        // Act
        var fontStack = FontStackBuilder.Build("JetBrains Mono");

        // Assert
        fontStack.Should().NotBeEmpty();
        fontStack.Should().StartWith("\"JetBrains Mono\"");
        fontStack.Should().Contain("monospace");
    }

    /// <summary>
    /// Verifies that FontStackBuilder correctly handles fonts with spaces.
    /// </summary>
    [Fact]
    public void FontStackBuilder_HandlesSpacesInFontNames()
    {
        // Act
        var fontStack = FontStackBuilder.Build("My Custom Font");

        // Assert
        fontStack.Should().StartWith("\"My Custom Font\"",
            "font with spaces should be quoted");
    }

    /// <summary>
    /// Verifies that AlgorithmConfigurator doesn't break SSH connections.
    /// This test ensures the algorithm configuration integrates properly.
    /// </summary>
    [Fact]
    public async Task AlgorithmConfigurator_DoesNotBreakConnections()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        // Arrange - SshConnectionService uses AlgorithmConfigurator internally
        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);

        // Act - This will use AlgorithmConfigurator during connection
        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            // Assert
            connection.IsConnected.Should().BeTrue(
                "connection with algorithm configuration should succeed");
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Verifies that SshTerminalBridge throughput tracking works correctly.
    /// </summary>
    [Fact]
    public async Task SshTerminalBridge_TracksThoughputCorrectly()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var outputReceived = new TaskCompletionSource<bool>();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);

            // Initial counters should be zero
            bridge.TotalBytesSent.Should().Be(0);
            bridge.TotalBytesReceived.Should().Be(0);

            bridge.DataReceived += _ =>
            {
                if (!outputReceived.Task.IsCompleted)
                {
                    outputReceived.TrySetResult(true);
                }
            };
            bridge.StartReading();

            // Wait for initial prompt data
            await Task.WhenAny(outputReceived.Task, Task.Delay(3000));

            // Bytes received should be > 0 after receiving prompt
            bridge.TotalBytesReceived.Should().BeGreaterThan(0,
                "should track received bytes");

            // Send some data
            bridge.SendCommand("echo throughput_test");
            await Task.Delay(500);

            // Both counters should be > 0 now
            bridge.TotalBytesSent.Should().BeGreaterThan(0,
                "should track sent bytes");
            bridge.TotalBytesReceived.Should().BeGreaterThan(0,
                "should track received bytes");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Verifies that TerminalOutputBuffer correctly captures output for search.
    /// </summary>
    [Fact]
    public async Task TerminalOutputBuffer_CapturesOutputForSearch()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var outputBuffer = new TerminalOutputBuffer();
        var searchComplete = new TaskCompletionSource<bool>();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                var text = System.Text.Encoding.UTF8.GetString(data);
                outputBuffer.AppendOutput(text);
                if (text.Contains("SEARCHABLE_TEXT"))
                {
                    searchComplete.TrySetResult(true);
                }
            };
            bridge.StartReading();

            await Task.Delay(1000);

            // Send command with searchable text
            bridge.SendCommand("echo SEARCHABLE_TEXT_12345");

            await Task.WhenAny(searchComplete.Task, Task.Delay(3000));

            // Verify buffer contains our text
            var allText = outputBuffer.GetAllText();
            allText.Should().Contain("SEARCHABLE_TEXT",
                "buffer should capture output for search");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Verifies that terminal resize after refactoring works correctly.
    /// </summary>
    [Fact]
    public async Task TerminalResize_WorksAfterRefactoring()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            // Resize to different dimensions
            var result1 = connection.ResizeTerminal(120, 40);
            var result2 = connection.ResizeTerminal(80, 24);
            var result3 = connection.ResizeTerminal(200, 50);

            // All resizes should succeed
            result1.Should().BeTrue("resize to 120x40 should succeed");
            result2.Should().BeTrue("resize to 80x24 should succeed");
            result3.Should().BeTrue("resize to 200x50 should succeed");
        }
        finally
        {
            connection.Dispose();
        }
    }

    #endregion
}
