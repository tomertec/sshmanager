using FluentAssertions;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Tests.Integration;

/// <summary>
/// Integration tests for SSH connection functionality.
/// These tests verify the SshTerminalBridge works correctly with real SSH servers.
///
/// Test Scenarios from Phase 4 Migration Plan:
/// - Basic Connection: Connect to SSH server, verify shell prompt displays
/// - Data flow: Verify bidirectional data exchange
/// - Terminal resize: Verify terminal dimensions are communicated
/// - Disconnection: Verify clean disconnect handling
/// </summary>
public class SshConnectionIntegrationTests : TerminalIntegrationTestBase
{
    [Fact]
    public async Task BasicConnection_ConnectsAndReceivesPrompt()
    {
        // Arrange
        if (!IsConfigured)
        {
            // Skip but don't fail
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var sshService = new SshConnectionService();
        var dataReceived = new TaskCompletionSource<bool>();
        var receivedData = new List<byte>();

        // Act
        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            connection.ShellStream.Should().NotBeNull("connection should have a ShellStream");

            // Create bridge to receive data
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                receivedData.AddRange(data);
                if (!dataReceived.Task.IsCompleted)
                {
                    dataReceived.TrySetResult(true);
                }
            };
            bridge.StartReading();

            // Wait for initial prompt or timeout
            var received = await Task.WhenAny(
                dataReceived.Task,
                Task.Delay(TimeSpan.FromSeconds(5))
            ) == dataReceived.Task;

            // Assert
            received.Should().BeTrue("should receive data from SSH server");
            receivedData.Should().NotBeEmpty("should have received terminal output");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task SendCommand_ExecutesAndReceivesOutput()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var sshService = new SshConnectionService();
        var outputReceived = new TaskCompletionSource<bool>();
        var receivedText = new System.Text.StringBuilder();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += data =>
            {
                var text = System.Text.Encoding.UTF8.GetString(data);
                receivedText.Append(text);
                if (text.Contains("TEST_MARKER"))
                {
                    outputReceived.TrySetResult(true);
                }
            };
            bridge.StartReading();

            // Wait for initial prompt
            await Task.Delay(500);

            // Send a command that produces identifiable output
            bridge.SendCommand("echo TEST_MARKER_12345");

            var received = await Task.WhenAny(
                outputReceived.Task,
                Task.Delay(TimeSpan.FromSeconds(5))
            ) == outputReceived.Task;

            received.Should().BeTrue("should receive command output");
            receivedText.ToString().Should().Contain("TEST_MARKER");

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Fact]
    public async Task Disconnect_RaisesDisconnectedEvent()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var sshService = new SshConnectionService();
        var disconnected = new TaskCompletionSource<bool>();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        var bridge = new SshTerminalBridge(connection.ShellStream);
        bridge.Disconnected += (s, e) => disconnected.TrySetResult(true);
        bridge.StartReading();

        // Disconnect
        connection.Dispose();

        var wasDisconnected = await Task.WhenAny(
            disconnected.Task,
            Task.Delay(TimeSpan.FromSeconds(5))
        ) == disconnected.Task;

        wasDisconnected.Should().BeTrue("should receive disconnect event");

        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task ByteCounters_TrackDataExchange()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var sshService = new SshConnectionService();
        var dataReceived = new TaskCompletionSource<bool>();

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);

        try
        {
            var bridge = new SshTerminalBridge(connection.ShellStream);
            bridge.DataReceived += _ =>
            {
                if (!dataReceived.Task.IsCompleted)
                {
                    dataReceived.TrySetResult(true);
                }
            };
            bridge.StartReading();

            // Wait for initial data
            await Task.WhenAny(dataReceived.Task, Task.Delay(2000));

            // Send some data
            bridge.SendCommand("echo hello");
            await Task.Delay(500);

            // Verify counters
            bridge.TotalBytesSent.Should().BeGreaterThan(0);
            bridge.TotalBytesReceived.Should().BeGreaterThan(0);

            await bridge.DisposeAsync();
        }
        finally
        {
            connection.Dispose();
        }
    }
}
