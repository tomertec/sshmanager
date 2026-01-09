using System.IO;
using System.Text.Json;

namespace SshManager.Terminal.Services.Playback;

/// <summary>
/// Represents the header metadata from an asciicast v2 recording.
/// </summary>
public sealed class AsciinemaHeader
{
    /// <summary>
    /// Format version (always 2 for asciicast v2).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Terminal width in columns.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Terminal height in rows.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Unix timestamp of when the recording was created.
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Optional title for the recording.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional environment variables (theme info, shell, etc).
    /// </summary>
    public Dictionary<string, string>? Env { get; set; }
}

/// <summary>
/// Represents a single event in an asciicast recording.
/// </summary>
/// <param name="Timestamp">Time offset in seconds from the start of recording.</param>
/// <param name="EventType">Event type: "o" for output, "i" for input.</param>
/// <param name="Data">The actual data (terminal output or user input).</param>
public sealed record RecordingEvent(double Timestamp, string EventType, string Data);

/// <summary>
/// Reads and parses asciicast v2 recording files.
/// </summary>
public sealed class AsciinemaReader
{
    /// <summary>
    /// Recording metadata from the header line.
    /// </summary>
    public AsciinemaHeader Header { get; private set; } = new();

    /// <summary>
    /// List of all events in the recording.
    /// </summary>
    public IReadOnlyList<RecordingEvent> Events { get; private set; } = Array.Empty<RecordingEvent>();

    /// <summary>
    /// Total duration of the recording.
    /// </summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Loads and parses an asciicast v2 recording file.
    /// </summary>
    /// <param name="filePath">Path to the .cast file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated AsciinemaReader instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file doesn't exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file format is invalid.</exception>
    public static async Task<AsciinemaReader> LoadAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Recording file not found: {filePath}", filePath);

        var reader = new AsciinemaReader();
        var events = new List<RecordingEvent>();
        var lineNumber = 0;
        var maxTimestamp = 0.0;

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var streamReader = new StreamReader(fileStream);

        while (!streamReader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await streamReader.ReadLineAsync(ct);
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (lineNumber == 1)
            {
                // First line is the header
                try
                {
                    reader.Header = JsonSerializer.Deserialize<AsciinemaHeader>(line)
                        ?? throw new InvalidDataException("Header deserialized to null");

                    if (reader.Header.Version != 2)
                        throw new InvalidDataException($"Unsupported asciicast version: {reader.Header.Version}. Only version 2 is supported.");

                    if (reader.Header.Width <= 0 || reader.Header.Height <= 0)
                        throw new InvalidDataException($"Invalid terminal dimensions: {reader.Header.Width}x{reader.Header.Height}");
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"Failed to parse header on line {lineNumber}: {ex.Message}", ex);
                }
            }
            else
            {
                // Subsequent lines are events: [timestamp, event_type, data]
                try
                {
                    using var jsonDoc = JsonDocument.Parse(line);
                    var root = jsonDoc.RootElement;

                    if (root.ValueKind != JsonValueKind.Array)
                        throw new InvalidDataException($"Line {lineNumber}: Expected JSON array");

                    if (root.GetArrayLength() < 3)
                        throw new InvalidDataException($"Line {lineNumber}: Event array must have at least 3 elements");

                    var timestamp = root[0].GetDouble();
                    var eventType = root[1].GetString() ?? string.Empty;
                    var data = root[2].GetString() ?? string.Empty;

                    events.Add(new RecordingEvent(timestamp, eventType, data));

                    if (timestamp > maxTimestamp)
                        maxTimestamp = timestamp;
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"Failed to parse event on line {lineNumber}: {ex.Message}", ex);
                }
            }
        }

        reader.Events = events.AsReadOnly();
        reader.Duration = TimeSpan.FromSeconds(maxTimestamp);

        return reader;
    }

    /// <summary>
    /// Gets events within a specific time range.
    /// </summary>
    /// <param name="startTime">Start time in seconds.</param>
    /// <param name="endTime">End time in seconds.</param>
    /// <returns>Events within the specified range.</returns>
    public IEnumerable<RecordingEvent> GetEventsInRange(double startTime, double endTime)
    {
        return Events.Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime);
    }

    /// <summary>
    /// Gets the index of the event at or after the specified timestamp.
    /// </summary>
    /// <param name="timestamp">Target timestamp in seconds.</param>
    /// <returns>Event index, or -1 if no events exist at or after the timestamp.</returns>
    public int GetEventIndexAtTime(double timestamp)
    {
        for (var i = 0; i < Events.Count; i++)
        {
            if (Events[i].Timestamp >= timestamp)
                return i;
        }
        return -1;
    }
}
