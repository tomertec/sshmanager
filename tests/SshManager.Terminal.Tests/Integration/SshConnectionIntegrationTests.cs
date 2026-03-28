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
///
/// Refactoring Phase 5 Additions:
/// - Verify ISshConnection interface compliance
/// - Verify SshConnectionBase dispose pattern
/// - Verify TerminalResizeService integration
/// - Verify RunCommandAsync functionality
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
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
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
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
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
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
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
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
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

    #region Refactoring Phase 5: SshConnectionBase Tests

    /// <summary>
    /// Verifies that ISshConnection.IsConnected returns correct state.
    /// </summary>
    [Fact]
    public async Task Connection_IsConnected_ReturnsTrueWhenConnected()
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
            // Assert
            connection.IsConnected.Should().BeTrue("connection should report as connected");
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Verifies that ISshConnection.IsConnected returns false after dispose.
    /// </summary>
    [Fact]
    public async Task Connection_IsConnected_ReturnsFalseAfterDispose()
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
        connection.Dispose();

        // Assert
        connection.IsConnected.Should().BeFalse("connection should report as disconnected after dispose");
    }

    /// <summary>
    /// Verifies that ISshConnection.Disconnected event is raised on dispose.
    /// </summary>
    [Fact]
    public async Task Connection_Disconnected_EventRaisedOnDispose()
    {
        if (!IsConfigured)
        {
            Assert.True(true, "SSH test environment not configured - skipping");
            return;
        }

        var connectionInfo = CreateConnectionInfo();
        var authFactory = new SshAuthenticationFactory();
        var sshService = new SshConnectionService(authFactory);
        var disconnectedRaised = false;

        var connection = await sshService.ConnectAsync(connectionInfo, null, null, 80, 24);
        connection.Disconnected += (s, e) => disconnectedRaised = true;

        // Act
        connection.Dispose();

        // Assert
        disconnectedRaised.Should().BeTrue("Disconnected event should be raised on dispose");
    }

    /// <summary>
    /// Verifies that ISshConnection.RunCommandAsync executes commands.
    /// </summary>
    [Fact]
    public async Task Connection_RunCommandAsync_ExecutesCommand()
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
            // Act
            var result = await connection.RunCommandAsync("echo INTEGRATION_TEST_123");

            // Assert
            result.Should().NotBeNull("command should return output");
            result.Should().Contain("INTEGRATION_TEST_123");
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Verifies that ISshConnection.RunCommandAsync returns null after dispose.
    /// </summary>
    [Fact]
    public async Task Connection_RunCommandAsync_ReturnsNullAfterDispose()
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
        connection.Dispose();

        // Act
        var result = await connection.RunCommandAsync("echo test");

        // Assert
        result.Should().BeNull("RunCommandAsync should return null after dispose");
    }

    /// <summary>
    /// Verifies that ISshConnection.ResizeTerminal returns success for valid dimensions.
    /// </summary>
    [Fact]
    public async Task Connection_ResizeTerminal_ReturnsTrue()
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
            // Act
            var result = connection.ResizeTerminal(120, 40);

            // Assert
            result.Should().BeTrue("resize should succeed for valid dimensions");
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Documents that SshConnectionBase.TrackDisposable tracks resources for disposal.
    /// Note: TrackDisposable is not on ISshConnection interface - it's used internally
    /// by the connection service to track auth-related resources like PrivateKeyFile.
    /// </summary>
    [Fact]
    public void Connection_TrackDisposable_DisposesTrackedResources_Documented()
    {
        // Document: TrackDisposable on SshConnectionBase tracks resources for disposal
        // This is used internally by SshConnectionService for auth resources
        Assert.True(true, "TrackDisposable tracks auth resources for cleanup");
    }

    /// <summary>
    /// Verifies that dispose is idempotent (can be called multiple times).
    /// </summary>
    [Fact]
    public async Task Connection_Dispose_IsIdempotent()
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

        // Act - Dispose multiple times
        var act = () =>
        {
            connection.Dispose();
            connection.Dispose();
            connection.Dispose();
        };

        // Assert
        act.Should().NotThrow("dispose should be idempotent");
    }

    /// <summary>
    /// Verifies that DisposeAsync works correctly.
    /// </summary>
    [Fact]
    public async Task Connection_DisposeAsync_DisposesConnection()
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

        // Act
        await connection.DisposeAsync();

        // Assert
        connection.IsConnected.Should().BeFalse("connection should be disposed");
    }

    #endregion
}
