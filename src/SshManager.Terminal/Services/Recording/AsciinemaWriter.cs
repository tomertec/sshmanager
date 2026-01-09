using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace SshManager.Terminal.Services.Recording;

/// <summary>
/// Thread-safe ASCIINEMA v2 format writer with buffering and periodic flushing.
/// Format: JSON header line, then [elapsed_seconds, event_type, data] JSON lines.
/// </summary>
public sealed class AsciinemaWriter : IAsyncDisposable
{
    private readonly string _filePath;
    private readonly ConcurrentQueue<(double Timestamp, string Type, string Data)> _buffer = new();
    private readonly System.Timers.Timer _flushTimer;
    private readonly DateTimeOffset _startTime;
    private StreamWriter? _writer;
    private bool _headerWritten;
    private readonly object _writeLock = new();
    private bool _disposed;

    private const int FlushIntervalMs = 100;
    private const int FlushThreshold = 1000;

    /// <summary>
    /// Total number of events written to the file.
    /// </summary>
    private long _eventCount;
    public long EventCount => _eventCount;

    /// <summary>
    /// Initializes a new ASCIINEMA writer.
    /// </summary>
    /// <param name="filePath">Path to the .cast file to write.</param>
    /// <param name="startTime">Recording start time (used for timestamp calculations).</param>
    public AsciinemaWriter(string filePath, DateTimeOffset startTime)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _startTime = startTime;

        // Ensure directory exists
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Create file stream and writer
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        _writer = new StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false);

        // Setup periodic flush timer
        _flushTimer = new System.Timers.Timer(FlushIntervalMs);
        _flushTimer.Elapsed += (_, _) => _ = FlushAsync();
        _flushTimer.AutoReset = true;
        _flushTimer.Start();
    }

    /// <summary>
    /// Writes the ASCIINEMA v2 header line.
    /// </summary>
    public async Task WriteHeaderAsync(int width, int height, string? title = null, Dictionary<string, string>? env = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_headerWritten)
        {
            throw new InvalidOperationException("Header already written");
        }

        var header = new
        {
            version = 2,
            width,
            height,
            timestamp = _startTime.ToUnixTimeSeconds(),
            title = title ?? "SSH Session",
            env = env ?? new Dictionary<string, string>
            {
                { "TERM", "xterm-256color" },
                { "SHELL", "/bin/bash" }
            }
        };

        lock (_writeLock)
        {
            if (_writer == null)
            {
                throw new InvalidOperationException("Writer is not initialized");
            }

            var headerJson = JsonSerializer.Serialize(header);
            _writer.WriteLine(headerJson);
            _headerWritten = true;
        }

        await FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Records terminal output data.
    /// </summary>
    /// <param name="data">Output text to record.</param>
    public void RecordOutput(string data)
    {
        if (_disposed || string.IsNullOrEmpty(data))
        {
            return;
        }

        double elapsed = (DateTimeOffset.UtcNow - _startTime).TotalSeconds;
        _buffer.Enqueue((elapsed, "o", data));
        Interlocked.Increment(ref _eventCount);

        // Trigger flush if buffer is large
        if (_buffer.Count >= FlushThreshold)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>
    /// Records user input data.
    /// </summary>
    /// <param name="data">Input text to record.</param>
    public void RecordInput(string data)
    {
        if (_disposed || string.IsNullOrEmpty(data))
        {
            return;
        }

        double elapsed = (DateTimeOffset.UtcNow - _startTime).TotalSeconds;
        _buffer.Enqueue((elapsed, "i", data));
        Interlocked.Increment(ref _eventCount);

        // Trigger flush if buffer is large
        if (_buffer.Count >= FlushThreshold)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>
    /// Flushes buffered events to disk.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_disposed || _writer == null)
        {
            return;
        }

        if (!_headerWritten)
        {
            return; // Wait for header to be written first
        }

        // Dequeue all pending events
        var eventsToWrite = new List<(double, string, string)>();
        while (_buffer.TryDequeue(out var evt))
        {
            eventsToWrite.Add(evt);
        }

        if (eventsToWrite.Count == 0)
        {
            return;
        }

        // Write events
        lock (_writeLock)
        {
            if (_writer == null || _disposed)
            {
                return;
            }

            foreach (var (timestamp, type, data) in eventsToWrite)
            {
                var eventArray = new object[] { timestamp, type, data };
                var eventJson = JsonSerializer.Serialize(eventArray);
                _writer.WriteLine(eventJson);
            }
        }

        // Flush to disk
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Writer was disposed during flush, ignore
        }
    }

    /// <summary>
    /// Disposes the writer and flushes any remaining buffered events.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop the flush timer
        _flushTimer?.Stop();
        _flushTimer?.Dispose();

        // Final flush
        await FlushAsync().ConfigureAwait(false);

        // Dispose writer
        lock (_writeLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
