using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace SshManager.Terminal.Services;

/// <summary>
/// Wraps an SSH client and shell stream as an ISshConnection for direct connections.
/// </summary>
internal sealed class SshConnection : SshConnectionBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SshConnection"/> class.
    /// </summary>
    /// <param name="client">The connected SSH client.</param>
    /// <param name="shellStream">The shell stream for terminal I/O.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="resizeService">Service for terminal resize operations.</param>
    public SshConnection(
        SshClient client,
        ShellStream shellStream,
        ILogger logger,
        ITerminalResizeService resizeService)
        : base(client, shellStream, logger, resizeService)
    {
    }

    // All functionality is provided by the base class
}
