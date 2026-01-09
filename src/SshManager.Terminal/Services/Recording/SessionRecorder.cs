using System.Buffers;
using System.IO;
using System.Text;

namespace SshManager.Terminal.Services.Recording;

/// <summary>
/// Records a single terminal session to an ASCIINEMA v2 format file.
/// Thread-safe and handles binary-to-text conversion for SSH output.
/// </summary>
public sealed class SessionRecorder : IAsyncDisposable
{
    private readonly AsciinemaWriter _writer;
    private readonly Decoder _decoder;
    private readonly object _decoderLock = new();
    private bool _disposed;

    /// <summary>
    /// Unique identifier for this recording.
    /// </summary>
    public Guid RecordingId { get; }

    /// <summary>
    /// Path to the .cast file being written.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Whether this recorder is actively recording.
    /// </summary>
    public bool IsRecording => !_disposed;

    /// <summary>
    /// When this recording started (UTC).
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    /// Duration of the recording so far.
    /// </summary>
    public TimeSpan Duration => DateTimeOffset.UtcNow - StartTime;

    /// <summary>
    /// Current file size in bytes.
    /// </summary>
    public long FileSizeBytes
    {
        get
        {
            try
            {
                return new FileInfo(FilePath).Length;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Total number of events recorded.
    /// </summary>
    public long EventCount => _writer.EventCount;

    /// <summary>
    /// Initializes a new session recorder.
    /// Private constructor - use CreateAsync factory method instead.
    /// </summary>
    private SessionRecorder(Guid recordingId, string filePath, AsciinemaWriter writer, DateTimeOffset startTime)
    {
        RecordingId = recordingId;
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        StartTime = startTime;
        _writer = writer;
        _decoder = Encoding.UTF8.GetDecoder();
    }

    /// <summary>
    /// Creates a new session recorder asynchronously.
    /// This is the preferred way to create a SessionRecorder to avoid blocking on async operations.
    /// </summary>
    /// <param name="recordingId">Unique ID for this recording.</param>
    /// <param name="filePath">Path to the .cast file to write.</param>
    /// <param name="width">Terminal width in columns.</param>
    /// <param name="height">Terminal height in rows.</param>
    /// <param name="title">Recording title (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new SessionRecorder instance.</returns>
    public static async Task<SessionRecorder> CreateAsync(
        Guid recordingId,
        string filePath,
        int width,
        int height,
        string? title = null,
        CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var writer = new AsciinemaWriter(filePath, startTime);
        await writer.WriteHeaderAsync(width, height, title).ConfigureAwait(false);

        return new SessionRecorder(recordingId, filePath, writer, startTime);
    }

    /// <summary>
    /// Records raw SSH output bytes. Converts to UTF-8 text before writing.
    /// </summary>
    /// <param name="data">Raw bytes from SSH stream.</param>
    public void RecordOutput(byte[] data)
    {
        if (_disposed || data == null || data.Length == 0)
        {
            return;
        }

        // Convert bytes to UTF-8 string using stateful decoder
        string text;
        lock (_decoderLock)
        {
            // Rent buffer for decoded characters
            int maxCharCount = _decoder.GetCharCount(data, 0, data.Length, flush: false);
            char[] charBuffer = ArrayPool<char>.Shared.Rent(maxCharCount);
            try
            {
                int charCount = _decoder.GetChars(data, 0, data.Length, charBuffer, 0, flush: false);
                text = new string(charBuffer, 0, charCount);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            _writer.RecordOutput(text);
        }
    }

    /// <summary>
    /// Records raw SSH output bytes. Converts to UTF-8 text before writing.
    /// </summary>
    /// <param name="data">Raw bytes from SSH stream.</param>
    /// <param name="offset">Start offset in the buffer.</param>
    /// <param name="count">Number of bytes to record.</param>
    public void RecordOutput(byte[] data, int offset, int count)
    {
        if (_disposed || data == null || count <= 0)
        {
            return;
        }

        // Convert bytes to UTF-8 string using stateful decoder
        string text;
        lock (_decoderLock)
        {
            // Rent buffer for decoded characters
            int maxCharCount = _decoder.GetCharCount(data, offset, count, flush: false);
            char[] charBuffer = ArrayPool<char>.Shared.Rent(maxCharCount);
            try
            {
                int charCount = _decoder.GetChars(data, offset, count, charBuffer, 0, flush: false);
                text = new string(charBuffer, 0, charCount);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            _writer.RecordOutput(text);
        }
    }

    /// <summary>
    /// Records user input text.
    /// </summary>
    /// <param name="input">Text typed by the user.</param>
    public void RecordInput(string input)
    {
        if (_disposed || string.IsNullOrEmpty(input))
        {
            return;
        }

        _writer.RecordInput(input);
    }

    /// <summary>
    /// Records a terminal resize event (ASCIINEMA extension).
    /// Some players support this, but it's not part of the core v2 spec.
    /// </summary>
    /// <param name="width">New terminal width.</param>
    /// <param name="height">New terminal height.</param>
    public void RecordResize(int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        // ASCIINEMA doesn't have a standard resize event type in v2
        // We can log it as an output event for debugging
        string resizeMsg = $"\x1b[8;{height};{width}t"; // DEC private mode resize sequence
        _writer.RecordOutput(resizeMsg);
    }

    /// <summary>
    /// Finalizes the recording by flushing any remaining data.
    /// Should be called before disposal when you want to ensure all data is written.
    /// </summary>
    public async Task FinalizeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // Flush decoder state
        lock (_decoderLock)
        {
            char[] finalChars = ArrayPool<char>.Shared.Rent(16);
            try
            {
                int charCount = _decoder.GetChars(Array.Empty<byte>(), 0, 0, finalChars, 0, flush: true);
                if (charCount > 0)
                {
                    string text = new string(finalChars, 0, charCount);
                    _writer.RecordOutput(text);
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(finalChars);
            }
        }

        // Flush writer
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the recorder and finalizes the recording file.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Finalize and dispose writer
        await FinalizeAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
