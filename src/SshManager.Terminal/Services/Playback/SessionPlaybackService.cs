namespace SshManager.Terminal.Services.Playback;

/// <summary>
/// Default implementation of session playback service.
/// </summary>
public sealed class SessionPlaybackService : ISessionPlaybackService
{
    /// <summary>
    /// Creates a playback controller for the specified recording file.
    /// </summary>
    /// <param name="filePath">Path to the asciicast recording file (.cast).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured playback controller ready to play the recording.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file doesn't exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file format is invalid.</exception>
    public async Task<PlaybackController> CreatePlaybackAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        var reader = await AsciinemaReader.LoadAsync(filePath, ct);
        return new PlaybackController(reader);
    }

    /// <summary>
    /// Loads and parses a recording file without creating a playback controller.
    /// Useful for inspecting recording metadata.
    /// </summary>
    /// <param name="filePath">Path to the asciicast recording file (.cast).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded recording data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file doesn't exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file format is invalid.</exception>
    public async Task<AsciinemaReader> LoadRecordingAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        return await AsciinemaReader.LoadAsync(filePath, ct);
    }
}
