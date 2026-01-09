namespace SshManager.Terminal.Services.Recording;

/// <summary>
/// Represents a single frame in a recorded terminal session.
/// Used for playback of ASCIINEMA recordings.
/// </summary>
public sealed class RecordingFrame
{
    /// <summary>
    /// Timestamp of this frame relative to the start of the recording.
    /// </summary>
    public TimeSpan Timestamp { get; }

    /// <summary>
    /// Event type: "o" for output, "i" for input.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Data content of this frame (terminal output or input text).
    /// </summary>
    public string Data { get; }

    /// <summary>
    /// Creates a new recording frame.
    /// </summary>
    /// <param name="timestamp">Time offset from recording start.</param>
    /// <param name="type">Event type (typically "o" or "i").</param>
    /// <param name="data">Frame data content.</param>
    public RecordingFrame(TimeSpan timestamp, string type, string data)
    {
        Timestamp = timestamp;
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
}
