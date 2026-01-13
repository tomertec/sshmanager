using System.Text;
using System.Text.RegularExpressions;

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

    // Constants
    private const int SegmentSize = 1000;

    // Regex to strip ANSI escape sequences
    // Matches: ESC followed by various control sequences:
    //   - C1 controls: ESC followed by single char in @-Z, \, _, or -
    //   - CSI sequences: ESC [ params command
    //   - OSC sequences: ESC ] ... BEL
    //   - DCS/PM sequences: ESC P ... ESC \
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B(?:[-@-Z\\_]|\[[0-?]*[ -/]*[@-~]|\][^\x07]*\x07|P[^\x1B]*\x1B\\)",
        RegexOptions.Compiled);

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

            // Strip ANSI escape sequences
            var cleanText = StripAnsiEscapes(text);

            foreach (var ch in cleanText)
            {
                if (ch == '\n')
                {
                    // End of line - store it in current segment
                    var line = _currentLine.ToString();
                    _currentSegment?.AppendLine(line);
                    _currentLine.Clear();

                    // Check if we need to rotate the current segment
                    if (_currentSegment?.LineCount >= SegmentSize)
                    {
                        RotateSegment();
                    }

                    // Trim excess lines across all segments
                    TrimExcess();
                }
                else if (ch == '\r')
                {
                    // Carriage return - move to start of line (common in terminal output)
                    // We handle this by clearing the current line if followed by non-newline
                    // For simplicity, we'll just ignore CR for now
                }
                else if (ch >= ' ' || ch == '\t')
                {
                    // Printable character or tab
                    _currentLine.Append(ch);
                }
            }
        }
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
            var totalLines = TotalLineCount;

            for (int i = 0; i < totalLines; i++)
            {
                sb.AppendLine(GetLine(i));
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
    /// Strips ANSI escape sequences from text.
    /// </summary>
    private static string StripAnsiEscapes(string text)
    {
        return AnsiEscapeRegex.Replace(text, string.Empty);
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

            // Archive this segment to disk asynchronously
            var lines = oldestMemorySegment.GetAllLines();
            var startIndex = oldestMemorySegment.StartLineIndex;

            // Create file segment (fire and forget - errors are silently ignored)
            _ = Task.Run(async () =>
            {
                try
                {
                    var fileSegment = await FileTerminalOutputSegment.CreateAsync(lines, startIndex);

                    // Replace the memory segment with the file segment
                    lock (_lock)
                    {
                        if (!_disposed && oldestIndex < _segments.Count && _segments[oldestIndex] == oldestMemorySegment)
                        {
                            _segments[oldestIndex].Dispose();
                            _segments[oldestIndex] = fileSegment;
                        }
                    }
                }
                catch
                {
                    // Archiving failed - keep the memory segment
                }
            });

            // Update counts
            linesInMemory -= oldestMemorySegment.LineCount;
            memorySegmentCount--;
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
