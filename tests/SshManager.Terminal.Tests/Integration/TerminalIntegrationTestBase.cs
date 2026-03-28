using SshManager.Core.Models;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Tests.Integration;

/// <summary>
/// Base class for terminal integration tests that require SSH connections.
/// Tests are skipped unless SSH_TEST_* environment variables are configured.
///
/// Required environment variables:
/// - SSH_TEST_HOST: Hostname or IP of the SSH server
/// - SSH_TEST_PORT: SSH port (default: 22)
/// - SSH_TEST_USER: Username for authentication
/// - SSH_TEST_PASSWORD: Password (for password auth)
/// - SSH_TEST_KEYFILE: Path to private key file (for key auth)
///
/// For local testing, consider using Docker:
/// docker run -d -p 2222:22 linuxserver/openssh-server
/// </summary>
public abstract class TerminalIntegrationTestBase : IAsyncLifetime
{
    protected string? TestHost { get; private set; }
    protected int TestPort { get; private set; } = 22;
    protected string? TestUser { get; private set; }
    protected string? TestPassword { get; private set; }
    protected string? TestKeyFile { get; private set; }

    protected bool IsConfigured => !string.IsNullOrEmpty(TestHost) && !string.IsNullOrEmpty(TestUser);

    public Task InitializeAsync()
    {
        TestHost = Environment.GetEnvironmentVariable("SSH_TEST_HOST");
        TestUser = Environment.GetEnvironmentVariable("SSH_TEST_USER");
        TestPassword = Environment.GetEnvironmentVariable("SSH_TEST_PASSWORD");
        TestKeyFile = Environment.GetEnvironmentVariable("SSH_TEST_KEYFILE");

        var portStr = Environment.GetEnvironmentVariable("SSH_TEST_PORT");
        if (int.TryParse(portStr, out var port))
        {
            TestPort = port;
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected void SkipIfNotConfigured()
    {
        if (!IsConfigured)
        {
            throw new SkipException("SSH test environment variables not configured. Set SSH_TEST_HOST and SSH_TEST_USER.");
        }
    }

    protected TerminalConnectionInfo CreateConnectionInfo()
    {
        SkipIfNotConfigured();

        var authType = !string.IsNullOrEmpty(TestKeyFile) ? AuthType.PrivateKeyFile :
                       !string.IsNullOrEmpty(TestPassword) ? AuthType.Password :
                       AuthType.SshAgent;

        return new TerminalConnectionInfo
        {
            Hostname = TestHost!,
            Port = TestPort,
            Username = TestUser!,
            AuthType = authType,
            Password = TestPassword,
            PrivateKeyPath = TestKeyFile
        };
    }

    protected HostEntry CreateHostEntry()
    {
        SkipIfNotConfigured();

        return new HostEntry
        {
            DisplayName = "Test Host",
            Hostname = TestHost!,
            Port = TestPort,
            Username = TestUser!,
            AuthType = !string.IsNullOrEmpty(TestKeyFile) ? AuthType.PrivateKeyFile :
                       !string.IsNullOrEmpty(TestPassword) ? AuthType.Password :
                       AuthType.SshAgent,
            PrivateKeyPath = TestKeyFile
        };
    }
}

/// <summary>
/// Custom exception for skipping tests.
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
