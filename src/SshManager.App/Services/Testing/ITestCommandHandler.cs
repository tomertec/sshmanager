#if DEBUG
namespace SshManager.App.Services.Testing;

/// <summary>
/// Interface for handling test automation commands.
/// </summary>
public interface ITestCommandHandler
{
    /// <summary>
    /// Handles a test command and returns a response.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response containing the result or error.</returns>
    Task<TestResponse> HandleCommandAsync(TestCommand command, CancellationToken cancellationToken = default);
}
#endif
