using System.Text;
using System.Threading.Channels;

namespace SshManager.Terminal;

/// <summary>
/// Stores terminal output text for search and logging functionality with lazy-loading support.
/// Uses tiered storage: recent lines kept in memory (hot tier), older lines compressed to disk (cold tier).
/// This reduces memory usage during long sessions while maintaining fast access to recent output.
/// </summary>
public sealed class TerminalOutputBuffer : IDisposable
{
    // Segmented storage
    private readonly List<ITerminalOutputSegment> _segments = new();
    private MemoryTerminalOutputSegment? _currentSegment;
    private readonly StringBuilder _currentLine = new();
    private readonly object _lock = new();
    private int _maxLines;
    private int _maxLinesInMemory;
    private bool _disposed;

    // Bounded channel for archive operations to prevent unbounded task growth
    private readonly Channel<ArchiveRequest> _archiveChannel;
    private readonly Task _archiveWorker;

    // Constants
    private const int SegmentSize = 1000;
    private const int MaxPendingArchives = 10; // Bounded queue capacity

    // Escape character constant for ANSI sequence detection
    private const char Escape = '\x1B';

    /// <summary>
    /// Request to archive a memory segment to disk.
    /// </summary>
    private sealed record ArchiveRequest(
        IReadOnlyList<string> Lines,
        int StartIndex,
        MemoryTerminalOutputSegment OriginalSegment,
        int SegmentIndex);

    /// <summary>
    /// Creates a new terminal output buffer with the specified maximum line count.
    /// </summary>
    /// <param name="maxLines">Maximum number of lines to retain (default: 10000).</param>
    /// <param name="maxLinesInMemory">Maximum number of lines to keep in memory (default: 5000). Older lines are archived to disk.</param>
    public TerminalOutputBuffer(int maxLines = 10000, int maxLinesInMemory = 5000)
    {
        _maxLines = Math.Max(100, maxLines);
        _maxLinesInMemory = Math.Max(100, Math.Min(maxLinesInMemory, maxLines));

        // Create initial segment
        _currentSegment = new MemoryTerminalOutputSegment(0);
        _segments.Add(_currentSegment);

        // Create bounded channel for archive operations
        _archiveChannel = Channel.CreateBounded<ArchiveRequest>(new BoundedChannelOptions(MaxPendingArchives)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest archives if queue is full
            SingleReader = true,
            SingleWriter = false
        });

        // Start the archive worker task
        _archiveWorker = Task.Run(ProcessArchiveRequestsAsync);
    }

    /// <summary>
    /// Gets or sets the maximum number of lines to retain across all segments.
    /// </summary>
    public int MaxLines
    {
        get => _maxLines;
        set => _maxLines = Math.Max(100, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of lines to keep in memory.
    /// Older lines are archived to compressed disk files.
    /// </summary>
    public int MaxLinesInMemory
    {
        get => _maxLinesInMemory;
        set => _maxLinesInMemory = Math.Max(100, Math.Min(value, _maxLines));
    }

    /// <summary>
    /// Gets the current number of lines in the buffer (legacy property for backward compatibility).
    /// Use TotalLineCount for the total across all segments.
    /// </summary>
    public int LineCount => TotalLineCount;

    /// <summary>
    /// Gets the total number of lines across all segments.
    /// </summary>
    public int TotalLineCount
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _segments.Sum(s => s.LineCount);
            }
        }
    }

    /// <summary>
    /// Appends text output from the terminal.
    /// ANSI escape sequences are stripped and the text is stored as plain text.
    /// Lines are added to the current in-memory segment. When the segment reaches capacity,
    /// it is rotated and older segments may be archived to disk.
    /// </summary>
    /// <param name="text">The terminal output text (may contain ANSI sequences).</param>
    public void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_lock)
        {
            ThrowIfDisposed();
            AppendOutputCore(text.AsSpan());
        }
    }

    /// <summary>
    /// Core implementation that processes a span of text, stripping ANSI sequences inline
    /// and batching line commits for efficiency.
    /// </summary>
    private void AppendOutputCore(ReadOnlySpan<char> text)
    {
        var linesAdded = 0;
        var i = 0;

        while (i < text.Length)
        {
            var ch = text[i];

            // Fast path: check for escape sequence start
            if (ch == Escape)
            {
                i = SkipAnsiSequence(text, i);
                continue;
            }

            if (ch == '\n')
            {
                // End of line - store it in current segment
                _currentSegment?.AppendLine(_currentLine.ToString());
                _currentLine.Clear();
                linesAdded++;
                i++;
                continue;
            }

            if (ch == '\r')
            {
                // Carriage return - ignore for now
                i++;
                continue;
            }

            if (ch >= ' ' || ch == '\t')
            {
                // Batch printable characters: find the end of the printable run
                var start = i;
                i++;
                while (i < text.Length)
                {
                    var next = text[i];
                    if (next == Escape || next == '\n' || next == '\r' || (next < ' ' && next != '\t'))
                        break;
                    i++;
                }
                // Append the entire run at once
#if NET6_0_OR_GREATER
                _currentLine.Append(text.Slice(start, i - start));
#else
                _currentLine.Append(text.Slice(start, i - start).ToString());
#endif
                continue;
            }

            // Skip other control characters
            i++;
        }

        // Batch segment rotation and trimming after processing
        if (linesAdded > 0)
        {
            // Check if we need to rotate the current segment
            if (_currentSegment?.LineCount >= SegmentSize)
            {
                RotateSegment();
            }

            // Trim excess lines across all segments
            TrimExcess();
        }
    }

    /// <summary>
    /// Skips an ANSI escape sequence starting at the given index.
    /// Returns the index of the first character after the sequence.
    /// </summary>
    private static int SkipAnsiSequence(ReadOnlySpan<char> text, int i)
    {
        // Must start with ESC
        if (i >= text.Length || text[i] != Escape)
            return i;

        i++; // Skip ESC
        if (i >= text.Length)
            return i;

        var next = text[i];

        // CSI sequence: ESC [ ... final byte (@-~)
        if (next == '[')
        {
            i++;
            // Skip parameter bytes (0x30-0x3F) and intermediate bytes (0x20-0x2F)
            while (i < text.Length)
            {
                var c = text[i];
                if (c >= 0x40 && c <= 0x7E) // Final byte
                {
                    i++;
                    break;
                }
                if (c < 0x20 || c > 0x3F) // Invalid/unknown, stop
                    break;
                i++;
            }
            return i;
        }

        // OSC sequence: ESC ] ... BEL or ESC ] ... ESC \
        if (next == ']')
        {
            i++;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\x07') // BEL terminates
                {
                    i++;
                    break;
                }
                if (c == Escape && i + 1 < text.Length && text[i + 1] == '\\') // ESC \ terminates
                {
                    i += 2;
                    break;
                }
                i++;
            }
            return i;
        }

        // DCS/PM sequence: ESC P ... ESC \
        if (next == 'P')
        {
            i++;
            while (i < text.Length)
            {
                if (text[i] == Escape && i + 1 < text.Length && text[i + 1] == '\\')
                {
                    i += 2;
                    break;
                }
                i++;
            }
            return i;
        }

        // C1 controls: ESC followed by single char in @-Z, \, _, or -
        if ((next >= '@' && next <= 'Z') || next == '\\' || next == '_' || next == '-')
        {
            return i + 1;
        }

        // Unknown sequence, just skip ESC
        return i;
    }

    /// <summary>
    /// Gets a specific line by index across all segments.
    /// This method transparently searches across segments and may trigger lazy loading of archived segments.
    /// </summary>
    /// <param name="index">The line index (0 = oldest line).</param>
    /// <returns>The line text, or empty string if index is out of range.</returns>
    public string GetLine(int index)
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            if (index < 0) return string.Empty;

            // Find the segment containing this line
            var currentIndex = 0;
            foreach (var segment in _segments)
            {
                if (index < currentIndex + segment.LineCount)
                {
                    // This segment contains the line
                    var relativeIndex = index - currentIndex;
                    return segment.GetLine(relativeIndex);
                }
                currentIndex += segment.LineCount;
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Gets a range of lines from the buffer across all segments.
    /// This method transparently searches across segments and may trigger lazy loading of archived segments.
    /// </summary>
    /// <param name="startIndex">Start index (0 = oldest line).</param>
    /// <param name="count">Number of lines to retrieve.</param>
    /// <returns>The requested lines.</returns>
    public IReadOnlyList<string> GetLines(int startIndex, int count)
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            var result = new List<string>();
            if (startIndex < 0 || count <= 0) return result;

            var totalLines = TotalLineCount;
            var end = Math.Min(startIndex + count, totalLines);

            // Iterate through segments to collect the requested lines
            var currentIndex = 0;
            foreach (var segment in _segments)
            {
                var segmentStartIndex = currentIndex;
                var segmentEndIndex = currentIndex + segment.LineCount;

                // Check if this segment overlaps with the requested range
                if (segmentEndIndex > startIndex && segmentStartIndex < end)
                {
                    // Calculate the range within this segment
                    var relativeStart = Math.Max(0, startIndex - segmentStartIndex);
                    var relativeEnd = Math.Min(segment.LineCount, end - segmentStartIndex);
                    var relativeCount = relativeEnd - relativeStart;

                    if (relativeCount > 0)
                    {
                        var segmentLines = segment.GetLines(relativeStart, relativeCount);
                        result.AddRange(segmentLines);
                    }
                }

                currentIndex += segment.LineCount;

                // Stop if we've collected enough lines
                if (currentIndex >= end) break;
            }

            return result;
        }
    }

    /// <summary>
    /// Gets all lines as a single string.
    /// Note: This may be memory-intensive for large buffers as it loads all segments.
    /// </summary>
    public string GetAllText()
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            var sb = new StringBuilder();

            // Iterate directly through segments to avoid repeated lookups
            foreach (var segment in _segments)
            {
                var lines = segment.GetLines(0, segment.LineCount);
                foreach (var line in lines)
                {
                    sb.AppendLine(line);
                }
            }

            if (_currentLine.Length > 0)
            {
                sb.Append(_currentLine);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Clears all lines from the buffer and disposes all segments.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            // Dispose all segments (including file cleanup)
            foreach (var segment in _segments)
            {
                segment.Dispose();
            }
            _segments.Clear();
            _currentLine.Clear();

            // Create new initial segment
            _currentSegment = new MemoryTerminalOutputSegment(0);
            _segments.Add(_currentSegment);
        }
    }

    /// <summary>
    /// Rotates the current segment by creating a new one and checking if old segments need archiving.
    /// </summary>
    private void RotateSegment()
    {
        if (_currentSegment == null) return;

        // Calculate the starting index for the new segment
        var newStartIndex = _segments.Sum(s => s.LineCount);

        // Create new current segment
        _currentSegment = new MemoryTerminalOutputSegment(newStartIndex);
        _segments.Add(_currentSegment);

        // Check if we need to archive old segments to disk
        ArchiveOldSegmentsIfNeeded();
    }

    /// <summary>
    /// Archives older memory segments to disk if the in-memory line count exceeds the limit.
    /// </summary>
    private void ArchiveOldSegmentsIfNeeded()
    {
        // Count total lines in memory segments
        var linesInMemory = 0;
        var memorySegmentCount = 0;

        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            if (_segments[i] is MemoryTerminalOutputSegment)
            {
                linesInMemory += _segments[i].LineCount;
                memorySegmentCount++;
            }
        }

        // If we exceed the memory limit, archive the oldest memory segment
        while (linesInMemory > _maxLinesInMemory && memorySegmentCount > 1)
        {
            // Find the oldest memory segment (excluding the current one)
            MemoryTerminalOutputSegment? oldestMemorySegment = null;
            int oldestIndex = -1;

            for (int i = 0; i < _segments.Count - 1; i++)
            {
                if (_segments[i] is MemoryTerminalOutputSegment memSeg)
                {
                    oldestMemorySegment = memSeg;
                    oldestIndex = i;
                    break;
                }
            }

            if (oldestMemorySegment == null || oldestIndex < 0) break;

            // Archive this segment to disk asynchronously via bounded channel
            var lines = oldestMemorySegment.GetAllLines();
            var startIndex = oldestMemorySegment.StartLineIndex;

            // Enqueue archive request (non-blocking, drops oldest if full)
            var request = new ArchiveRequest(lines, startIndex, oldestMemorySegment, oldestIndex);
            _archiveChannel.Writer.TryWrite(request);

            // Update counts
            linesInMemory -= oldestMemorySegment.LineCount;
            memorySegmentCount--;
        }
    }

    /// <summary>
    /// Background worker that processes archive requests from the bounded channel.
    /// </summary>
    private async Task ProcessArchiveRequestsAsync()
    {
        try
        {
            await foreach (var request in _archiveChannel.Reader.ReadAllAsync())
            {
                if (_disposed) break;

                FileTerminalOutputSegment? fileSegment = null;
                try
                {
                    fileSegment = await FileTerminalOutputSegment.CreateAsync(request.Lines, request.StartIndex);

                    // Replace the memory segment with the file segment
                    lock (_lock)
                    {
                        if (!_disposed && request.SegmentIndex < _segments.Count && 
                            _segments[request.SegmentIndex] == request.OriginalSegment)
                        {
                            _segments[request.SegmentIndex].Dispose();
                            _segments[request.SegmentIndex] = fileSegment;
                            fileSegment = null; // Transferred ownership
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to archive terminal output: {ex.Message}");
                }
                finally
                {
                    // If we created a file segment but couldn't store it, dispose it
                    fileSegment?.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when channel is completed during disposal
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Archive worker error: {ex.Message}");
        }
    }

    /// <summary>
    /// Trims excess lines from the beginning of the buffer by removing old segments or partial lines.
    /// </summary>
    private void TrimExcess()
    {
        var totalLines = _segments.Sum(s => s.LineCount);
        var excess = totalLines - _maxLines;

        if (excess <= 0) return;

        // Remove entire segments from the beginning until we're under the limit
        while (excess > 0 && _segments.Count > 1)
        {
            var firstSegment = _segments[0];

            if (excess >= firstSegment.LineCount)
            {
                // Remove entire segment
                var linesRemoved = firstSegment.LineCount;
                _segments.RemoveAt(0);
                firstSegment.Dispose();
                excess -= linesRemoved;

                // Update start indices for remaining segments
                UpdateSegmentStartIndices();
            }
            else
            {
                // Can't remove entire segment, break and handle partial below
                break;
            }
        }

        // Handle remaining excess by trimming from the first segment
        if (excess > 0 && _segments.Count > 0 && _segments[0] is MemoryTerminalOutputSegment memSegment)
        {
            memSegment.TrimFromFront(excess);
            UpdateSegmentStartIndices();
        }
    }

    /// <summary>
    /// Updates the start indices of all segments after segment removal.
    /// </summary>
    private void UpdateSegmentStartIndices()
    {
        var currentIndex = 0;
        foreach (var segment in _segments)
        {
            if (segment is MemoryTerminalOutputSegment memSeg)
            {
                memSeg.UpdateStartIndex(currentIndex);
            }
            currentIndex += segment.LineCount;
        }
    }

    /// <summary>
    /// Throws ObjectDisposedException if the buffer has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputBuffer));
        }
    }

    /// <summary>
    /// Disposes the buffer and cleans up all segments and temp files.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;

            _disposed = true;

            // Complete the channel to signal the worker to stop
            _archiveChannel.Writer.TryComplete();
        }

        // Wait for the archive worker to finish (outside lock to avoid deadlock)
        try
        {
            _archiveWorker.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Ignore exceptions during shutdown
        }

        lock (_lock)
        {
            // Dispose all segments (including file cleanup)
            foreach (var segment in _segments)
            {
                segment.Dispose();
            }
            _segments.Clear();
            _currentSegment = null;
            _currentLine.Clear();
        }
    }
}
