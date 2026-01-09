using SshManager.Core.Models;

namespace SshManager.Terminal.Services.Recording;

/// <summary>
/// Service for managing terminal session recordings in ASCIINEMA v2 format.
/// </summary>
public interface ISessionRecordingService
{
    /// <summary>
    /// Gets the directory where recordings are stored.
    /// </summary>
    string RecordingsDirectory { get; }

    /// <summary>
    /// Starts recording a terminal session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="host">Host being connected to (optional).</param>
    /// <param name="cols">Terminal width in columns.</param>
    /// <param name="rows">Terminal height in rows.</param>
    /// <param name="title">Recording title (optional, defaults to host info).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active session recorder.</returns>
    Task<SessionRecorder> StartRecordingAsync(
        Guid sessionId,
        HostEntry? host,
        int cols,
        int rows,
        string? title = null,
        CancellationToken ct = default);

    /// <summary>
    /// Stops recording a session and finalizes the recording file.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopRecordingAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the active recorder for a session, if any.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>The recorder, or null if not recording.</returns>
    SessionRecorder? GetRecorder(Guid sessionId);

    /// <summary>
    /// Checks if a session is currently being recorded.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>True if recording, false otherwise.</returns>
    bool IsRecording(Guid sessionId);

    /// <summary>
    /// Loads a recording from disk for playback.
    /// </summary>
    /// <param name="recordingId">Recording identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of recording frames in chronological order.</returns>
    Task<List<RecordingFrame>> LoadRecordingAsync(Guid recordingId, CancellationToken ct = default);
}
