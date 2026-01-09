namespace SshManager.Terminal.Services;

/// <summary>
/// Service for broadcasting keyboard input to multiple terminal sessions.
/// </summary>
public interface IBroadcastInputService
{
    /// <summary>
    /// Gets or sets whether broadcast mode is enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Sends data to all sessions selected for broadcast.
    /// </summary>
    /// <param name="data">The data bytes to send.</param>
    void SendToSelected(byte[] data);

    /// <summary>
    /// Sends data to all connected sessions.
    /// </summary>
    /// <param name="data">The data bytes to send.</param>
    void SendToAll(byte[] data);

    /// <summary>
    /// Gets the number of sessions that will receive broadcast input.
    /// </summary>
    int TargetSessionCount { get; }
}
