#if DEBUG
namespace SshManager.App.Services.Testing;

/// <summary>
/// Interface for the test automation server that allows external tools
/// (like Claude Code) to interact with the application.
/// </summary>
public interface ITestServer : IDisposable
{
    /// <summary>
    /// Whether the server is currently running and accepting connections.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// The name of the named pipe the server is listening on.
    /// </summary>
    string PipeName { get; }

    /// <summary>
    /// Starts the test server and begins listening for commands.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop the server.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the test server gracefully.
    /// </summary>
    Task StopAsync();
}
#endif
