namespace SshManager.Terminal;

/// <summary>
/// Represents a segment of terminal output lines with lazy-loading support.
/// Segments can be stored in memory or backed by disk files.
/// </summary>
public interface ITerminalOutputSegment : IDisposable
{
    /// <summary>
    /// Gets the number of lines in this segment.
    /// </summary>
    int LineCount { get; }

    /// <summary>
    /// Gets the starting line index of this segment in the overall buffer.
    /// </summary>
    int StartLineIndex { get; }

    /// <summary>
    /// Gets whether the segment data is currently loaded in memory.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Gets a specific line by its relative index within this segment.
    /// </summary>
    /// <param name="relativeIndex">The line index relative to this segment (0 to LineCount-1).</param>
    /// <returns>The line text, or empty string if index is out of range.</returns>
    string GetLine(int relativeIndex);

    /// <summary>
    /// Gets a range of lines from this segment.
    /// </summary>
    /// <param name="relativeStartIndex">Start index relative to this segment.</param>
    /// <param name="count">Number of lines to retrieve.</param>
    /// <returns>The requested lines.</returns>
    IReadOnlyList<string> GetLines(int relativeStartIndex, int count);

    /// <summary>
    /// Loads the segment data into memory if not already loaded.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Unloads the segment data from memory (file-backed segments only).
    /// The segment can be loaded again later via LoadAsync.
    /// </summary>
    void Unload();
}
