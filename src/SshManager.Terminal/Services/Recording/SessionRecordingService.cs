using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using SshManager.Core.Models;
using SshManager.Data;
using SshManager.Data.Repositories;

namespace SshManager.Terminal.Services.Recording;

/// <summary>
/// Service for managing terminal session recordings in ASCIINEMA v2 format.
/// Handles recording lifecycle, file storage, and database tracking.
/// </summary>
public sealed partial class SessionRecordingService : ISessionRecordingService
{
    private readonly ISessionRecordingRepository _recordingRepository;
    private readonly ConcurrentDictionary<Guid, SessionRecorder> _activeRecorders = new();

    /// <summary>
    /// Directory where recording files are stored.
    /// </summary>
    public string RecordingsDirectory { get; }

    /// <summary>
    /// Initializes a new session recording service.
    /// </summary>
    /// <param name="recordingRepository">Repository for recording metadata.</param>
    public SessionRecordingService(ISessionRecordingRepository recordingRepository)
    {
        _recordingRepository = recordingRepository ?? throw new ArgumentNullException(nameof(recordingRepository));

        // Use same app data directory as database
        RecordingsDirectory = Path.Combine(DbPaths.GetAppDataDir(), "recordings");
        Directory.CreateDirectory(RecordingsDirectory);
    }

    /// <summary>
    /// Starts recording a terminal session.
    /// </summary>
    public async Task<SessionRecorder> StartRecordingAsync(
        Guid sessionId,
        HostEntry? host,
        int cols,
        int rows,
        string? title = null,
        CancellationToken ct = default)
    {
        // Check if already recording
        if (_activeRecorders.ContainsKey(sessionId))
        {
            throw new InvalidOperationException($"Session {sessionId} is already being recorded");
        }

        // Generate filename and title
        string filename = GenerateFilename(host);
        string filePath = Path.Combine(RecordingsDirectory, filename);
        string recordingTitle = title ?? GenerateTitle(host);

        // Create database entry
        var recordingId = Guid.NewGuid();
        var recording = new SessionRecording
        {
            Id = recordingId,
            HostId = host?.Id,
            Title = recordingTitle,
            FileName = filename,
            TerminalWidth = cols,
            TerminalHeight = rows,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = null,
            Duration = TimeSpan.Zero,
            FileSizeBytes = 0,
            EventCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _recordingRepository.AddAsync(recording, ct).ConfigureAwait(false);

        // Create recorder using factory method
        var recorder = await SessionRecorder.CreateAsync(recordingId, filePath, cols, rows, recordingTitle, ct).ConfigureAwait(false);

        // Track active recorder
        if (!_activeRecorders.TryAdd(sessionId, recorder))
        {
            // Race condition - another thread started recording
            await recorder.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Session {sessionId} is already being recorded");
        }

        return recorder;
    }

    /// <summary>
    /// Stops recording a session and finalizes the recording file.
    /// </summary>
    public async Task StopRecordingAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!_activeRecorders.TryRemove(sessionId, out var recorder))
        {
            return; // Not recording
        }

        try
        {
            // Finalize recording
            await recorder.FinalizeAsync().ConfigureAwait(false);

            // Update database with final stats
            var duration = recorder.Duration;
            var fileSize = recorder.FileSizeBytes;
            var eventCount = recorder.EventCount;

            await _recordingRepository.UpdateDurationAndSizeAsync(
                recorder.RecordingId,
                duration,
                fileSize,
                eventCount,
                ct).ConfigureAwait(false);
        }
        finally
        {
            // Dispose recorder
            await recorder.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the active recorder for a session, if any.
    /// </summary>
    public SessionRecorder? GetRecorder(Guid sessionId)
    {
        _activeRecorders.TryGetValue(sessionId, out var recorder);
        return recorder;
    }

    /// <summary>
    /// Checks if a session is currently being recorded.
    /// </summary>
    public bool IsRecording(Guid sessionId)
    {
        return _activeRecorders.ContainsKey(sessionId);
    }

    /// <summary>
    /// Loads a recording from disk for playback.
    /// </summary>
    public async Task<List<RecordingFrame>> LoadRecordingAsync(Guid recordingId, CancellationToken ct = default)
    {
        // Get recording metadata
        var recording = await _recordingRepository.GetByIdAsync(recordingId, ct).ConfigureAwait(false);
        if (recording == null)
        {
            throw new InvalidOperationException($"Recording {recordingId} not found");
        }

        // Build file path
        string filePath = Path.Combine(RecordingsDirectory, recording.FileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Recording file not found: {filePath}");
        }

        // Parse ASCIINEMA v2 file
        var frames = new List<RecordingFrame>();

        using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);

        // Skip header line
        await reader.ReadLineAsync(ct).ConfigureAwait(false);

        // Read event lines
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                // Parse JSON array: [elapsed_seconds, event_type, data]
                var eventArray = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line);

                if (eventArray.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                var items = eventArray.EnumerateArray().ToList();
                if (items.Count < 3)
                {
                    continue;
                }

                double elapsedSeconds = items[0].GetDouble();
                string eventType = items[1].GetString() ?? "o";
                string data = items[2].GetString() ?? "";

                var timestamp = TimeSpan.FromSeconds(elapsedSeconds);
                frames.Add(new RecordingFrame(timestamp, eventType, data));
            }
            catch (System.Text.Json.JsonException)
            {
                // Skip malformed lines
                continue;
            }
        }

        return frames;
    }

    /// <summary>
    /// Generates a filename for a recording based on host info and timestamp.
    /// Format: {sanitized_hostname}_{yyyyMMdd-HHmmss}.cast
    /// </summary>
    private static string GenerateFilename(HostEntry? host)
    {
        string hostname = host?.Hostname ?? "unknown";
        string sanitized = SanitizeFilename(hostname);
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return $"{sanitized}_{timestamp}.cast";
    }

    /// <summary>
    /// Generates a display title for a recording.
    /// </summary>
    private static string GenerateTitle(HostEntry? host)
    {
        if (host == null)
        {
            return $"SSH Session - {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }

        string userHost = string.IsNullOrEmpty(host.Username)
            ? host.Hostname
            : $"{host.Username}@{host.Hostname}";

        return $"{userHost} - {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
    }

    /// <summary>
    /// Sanitizes a string for use as a filename by removing invalid characters.
    /// </summary>
    private static string SanitizeFilename(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "recording";
        }

        // Replace invalid filename characters with underscores
        string sanitized = InvalidFilenameCharsRegex().Replace(input, "_");

        // Limit length
        if (sanitized.Length > 50)
        {
            sanitized = sanitized[..50];
        }

        // Ensure it's not empty after sanitization
        return string.IsNullOrWhiteSpace(sanitized) ? "recording" : sanitized;
    }

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex InvalidFilenameCharsRegex();
}
