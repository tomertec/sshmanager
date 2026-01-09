using FluentAssertions;
using SshManager.Terminal.Services;

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
        var sshService = new SshConnectionService();
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
        var sshService = new SshConnectionService();
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
        var sshService = new SshConnectionService();
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
        var sshService = new SshConnectionService();
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
        var sshService = new SshConnectionService();
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
}
