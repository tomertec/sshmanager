namespace SshManager.Terminal;

/// <summary>
/// In-memory segment for hot terminal output lines.
/// This segment type is always loaded and does not perform any file I/O.
/// Used for the most recent lines to ensure fast access.
/// </summary>
public sealed class MemoryTerminalOutputSegment : ITerminalOutputSegment
{
    private readonly List<string> _lines = new();
    private readonly object _lock = new();
    private int _startLineIndex;
    private bool _disposed;

    /// <summary>
    /// Creates a new in-memory segment.
    /// </summary>
    /// <param name="startLineIndex">The starting line index in the overall buffer.</param>
    public MemoryTerminalOutputSegment(int startLineIndex)
    {
        _startLineIndex = startLineIndex;
    }

    /// <inheritdoc />
    public int LineCount
    {
        get
        {
            lock (_lock)
            {
                return _lines.Count;
            }
        }
    }

    /// <inheritdoc />
    public int StartLineIndex
    {
        get
        {
            lock (_lock)
            {
                return _startLineIndex;
            }
        }
    }

    /// <inheritdoc />
    public bool IsLoaded => true;

    /// <summary>
    /// Appends a line to this segment.
    /// </summary>
    /// <param name="line">The line to append.</param>
    public void AppendLine(string line)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            _lines.Add(line);
        }
    }

    /// <summary>
    /// Updates the starting line index when segments are rotated.
    /// </summary>
    /// <param name="newStartIndex">The new starting index.</param>
    internal void UpdateStartIndex(int newStartIndex)
    {
        lock (_lock)
        {
            _startLineIndex = newStartIndex;
        }
    }

    /// <inheritdoc />
    public string GetLine(int relativeIndex)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (relativeIndex >= 0 && relativeIndex < _lines.Count)
            {
                return _lines[relativeIndex];
            }
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetLines(int relativeStartIndex, int count)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            var result = new List<string>();
            var end = Math.Min(relativeStartIndex + count, _lines.Count);

            for (var i = relativeStartIndex; i < end; i++)
            {
                if (i >= 0 && i < _lines.Count)
                {
                    result.Add(_lines[i]);
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    public Task LoadAsync(CancellationToken ct = default)
    {
        // Memory segments are always loaded
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Unload()
    {
        // Memory segments cannot be unloaded
    }

    /// <summary>
    /// Gets all lines as a read-only list (for archiving to file).
    /// </summary>
    internal IReadOnlyList<string> GetAllLines()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            return _lines.ToArray();
        }
    }

    /// <summary>
    /// Removes lines from the front of the segment.
    /// </summary>
    /// <param name="count">Number of lines to remove.</param>
    internal void TrimFromFront(int count)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (count <= 0) return;
            if (count >= _lines.Count)
            {
                _lines.Clear();
            }
            else
            {
                _lines.RemoveRange(0, count);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _lines.Clear();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MemoryTerminalOutputSegment));
        }
    }
}
