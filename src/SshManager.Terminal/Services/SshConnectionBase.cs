using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SshManager.Terminal.Services;

/// <summary>
/// Base class for SSH connections providing common functionality for
/// shell stream management, terminal resize, command execution, and disposal.
/// </summary>
public abstract class SshConnectionBase : ISshConnection
{
    /// <summary>
    /// The primary SSH client for this connection (target client for proxy chains).
    /// </summary>
    protected readonly SshClient Client;

    /// <summary>
    /// Logger instance for diagnostic output.
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Service for handling terminal resize operations.
    /// </summary>
    protected readonly ITerminalResizeService ResizeService;

    /// <summary>
    /// Tracked disposable resources that will be disposed when this connection is closed.
    /// </summary>
    protected readonly List<IDisposable> Disposables = new();

    /// <summary>
    /// Indicates whether this connection has been disposed.
    /// </summary>
    protected bool Disposed;

    /// <summary>
    /// Gets the shell stream for terminal I/O.
    /// </summary>
    public ShellStream ShellStream { get; }

    /// <summary>
    /// Gets whether the connection is currently active.
    /// </summary>
    public bool IsConnected => Client.IsConnected && !Disposed;

    /// <summary>
    /// Event raised when the connection is closed.
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshConnectionBase"/> class.
    /// </summary>
    /// <param name="client">The SSH client for this connection.</param>
    /// <param name="shellStream">The shell stream for terminal I/O.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="resizeService">Service for terminal resize operations.</param>
    protected SshConnectionBase(
        SshClient client,
        ShellStream shellStream,
        ILogger logger,
        ITerminalResizeService resizeService)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        ShellStream = shellStream ?? throw new ArgumentNullException(nameof(shellStream));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ResizeService = resizeService ?? throw new ArgumentNullException(nameof(resizeService));

        // Subscribe to error/disconnect events
        Client.ErrorOccurred += OnClientError;
        ShellStream.Closed += OnStreamClosed;
    }

    /// <summary>
    /// Registers a disposable resource to be disposed when this connection is closed.
    /// Used to track PrivateKeyFile instances and other resources that need cleanup.
    /// </summary>
    /// <param name="disposable">The disposable resource to track.</param>
    public void TrackDisposable(IDisposable disposable)
    {
        if (disposable != null)
        {
            Disposables.Add(disposable);
        }
    }

    /// <summary>
    /// Resizes the terminal window on the remote host.
    /// </summary>
    /// <param name="columns">The new terminal width in columns.</param>
    /// <param name="rows">The new terminal height in rows.</param>
    /// <returns>True if the resize was successful, false otherwise.</returns>
    public bool ResizeTerminal(uint columns, uint rows)
    {
        return ResizeService.TryResize(ShellStream, columns, rows);
    }

    /// <summary>
    /// Gets the underlying SSH client for advanced operations such as port forwarding.
    /// </summary>
    /// <returns>The SSH client instance.</returns>
    public SshClient GetSshClient()
    {
        return Client;
    }

    /// <summary>
    /// Runs a command on the server and returns the output.
    /// This uses a separate channel and does not interfere with the shell stream.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="timeout">Timeout for command execution. Defaults to 5 seconds.</param>
    /// <returns>The command output, or null if execution failed.</returns>
    public async Task<string?> RunCommandAsync(string command, TimeSpan? timeout = null)
    {
        if (Disposed || !Client.IsConnected)
        {
            return null;
        }

        try
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);

            return await Task.Run(() =>
            {
                using var cmd = Client.CreateCommand(command);
                cmd.CommandTimeout = actualTimeout;
                var result = cmd.Execute();
                return result?.Trim();
            });
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to run command: {Command}", command);
            return null;
        }
    }

    /// <summary>
    /// Handles errors from the SSH client.
    /// </summary>
    protected virtual void OnClientError(object? sender, ExceptionEventArgs e)
    {
        Logger.LogWarning(e.Exception, "SSH connection error occurred");
        RaiseDisconnected();
    }

    /// <summary>
    /// Handles the shell stream being closed.
    /// </summary>
    protected virtual void OnStreamClosed(object? sender, EventArgs e)
    {
        Logger.LogInformation("SSH shell stream closed");
        RaiseDisconnected();
    }

    /// <summary>
    /// Raises the <see cref="Disconnected"/> event.
    /// </summary>
    protected void RaiseDisconnected()
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the core disposal logic. Override in derived classes for additional cleanup.
    /// </summary>
    protected virtual void DisposeCore()
    {
        Logger.LogDebug("Disposing SSH connection");

        // Unsubscribe from events
        Client.ErrorOccurred -= OnClientError;
        ShellStream.Closed -= OnStreamClosed;

        // Dispose shell stream first
        try
        {
            ShellStream.Dispose();
            Logger.LogDebug("Shell stream disposed");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error disposing shell stream");
        }

        // Disconnect and dispose client
        try
        {
            if (Client.IsConnected)
            {
                Client.Disconnect();
                Logger.LogDebug("SSH client disconnected");
            }
            Client.Dispose();
            Logger.LogDebug("SSH client disposed");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error disposing SSH client");
        }

        // Dispose tracked resources (PrivateKeyFile instances)
        DisposeTrackedResources();
    }

    /// <summary>
    /// Disposes all tracked disposable resources.
    /// </summary>
    protected void DisposeTrackedResources()
    {
        var disposableCount = Disposables.Count;
        foreach (var disposable in Disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Error disposing tracked resource");
            }
        }
        Disposables.Clear();
        if (disposableCount > 0)
        {
            Logger.LogDebug("Tracked disposables disposed ({Count} items)", disposableCount);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;

        DisposeCore();

        Logger.LogInformation("SSH connection disposed");
        RaiseDisconnected();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Task.Run(Dispose);
    }
}
