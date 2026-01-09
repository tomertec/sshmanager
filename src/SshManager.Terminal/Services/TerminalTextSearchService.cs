namespace SshManager.Terminal.Services;

/// <summary>
/// Represents a search match in the terminal output buffer.
/// </summary>
public sealed class TextSearchMatch
{
    /// <summary>Line index in the buffer.</summary>
    public int LineIndex { get; init; }

    /// <summary>Character start position within the line.</summary>
    public int StartColumn { get; init; }

    /// <summary>Length of the match in characters.</summary>
    public int Length { get; init; }

    /// <summary>The matched text.</summary>
    public string MatchedText { get; init; } = string.Empty;
}

/// <summary>
/// Service for searching through terminal output buffer content.
/// This version works with text-based TerminalOutputBuffer instead of VtNetCore types.
/// </summary>
public sealed class TerminalTextSearchService
{
    private readonly TerminalOutputBuffer _buffer;
    private List<TextSearchMatch> _matches = new();
    private int _currentMatchIndex = -1;
    private string _lastSearchTerm = "";
    private bool _lastCaseSensitive;

    public TerminalTextSearchService(TerminalOutputBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>Current list of matches.</summary>
    public IReadOnlyList<TextSearchMatch> Matches => _matches;

    /// <summary>Index of the currently highlighted match (-1 if none).</summary>
    public int CurrentMatchIndex => _currentMatchIndex;

    /// <summary>The current match, or null if none.</summary>
    public TextSearchMatch? CurrentMatch =>
        _currentMatchIndex >= 0 && _currentMatchIndex < _matches.Count
            ? _matches[_currentMatchIndex]
            : null;

    /// <summary>Total number of matches found.</summary>
    public int MatchCount => _matches.Count;

    /// <summary>
    /// Search the buffer for the given term.
    /// </summary>
    public void Search(string searchTerm, bool caseSensitive)
    {
        _matches.Clear();
        _currentMatchIndex = -1;
        _lastSearchTerm = searchTerm;
        _lastCaseSensitive = caseSensitive;

        if (string.IsNullOrEmpty(searchTerm)) return;

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var lineCount = _buffer.LineCount;
        for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var line = _buffer.GetLine(lineIndex);
            if (string.IsNullOrEmpty(line)) continue;

            int position = 0;
            while (position < line.Length)
            {
                int matchIndex = line.IndexOf(searchTerm, position, comparison);
                if (matchIndex < 0) break;

                _matches.Add(new TextSearchMatch
                {
                    LineIndex = lineIndex,
                    StartColumn = matchIndex,
                    Length = searchTerm.Length,
                    MatchedText = line.Substring(matchIndex, searchTerm.Length)
                });

                position = matchIndex + 1;
            }
        }

        // Move to first match if found
        if (_matches.Count > 0)
        {
            _currentMatchIndex = 0;
        }
    }

    /// <summary>
    /// Move to the next match. Wraps around to the beginning.
    /// </summary>
    public bool NextMatch()
    {
        if (_matches.Count == 0) return false;

        _currentMatchIndex = (_currentMatchIndex + 1) % _matches.Count;
        return true;
    }

    /// <summary>
    /// Move to the previous match. Wraps around to the end.
    /// </summary>
    public bool PreviousMatch()
    {
        if (_matches.Count == 0) return false;

        _currentMatchIndex = (_currentMatchIndex - 1 + _matches.Count) % _matches.Count;
        return true;
    }

    /// <summary>
    /// Clear search results.
    /// </summary>
    public void ClearSearch()
    {
        _matches.Clear();
        _currentMatchIndex = -1;
        _lastSearchTerm = "";
    }

    /// <summary>
    /// Check if a specific position should be highlighted as a match.
    /// </summary>
    public bool IsHighlighted(int lineIndex, int column, out bool isCurrentMatch)
    {
        isCurrentMatch = false;

        for (int i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            if (match.LineIndex == lineIndex &&
                column >= match.StartColumn &&
                column < match.StartColumn + match.Length)
            {
                isCurrentMatch = (i == _currentMatchIndex);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get matches visible in the current viewport.
    /// </summary>
    public IEnumerable<TextSearchMatch> GetMatchesInRange(int startLine, int lineCount)
    {
        return _matches.Where(m =>
            m.LineIndex >= startLine &&
            m.LineIndex < startLine + lineCount);
    }

    /// <summary>
    /// Refresh search results using the last search parameters.
    /// Useful when the buffer content changes.
    /// </summary>
    public void RefreshSearch()
    {
        if (!string.IsNullOrEmpty(_lastSearchTerm))
        {
            var previousMatchIndex = _currentMatchIndex;
            Search(_lastSearchTerm, _lastCaseSensitive);

            // Try to restore position if possible
            if (previousMatchIndex >= 0 && previousMatchIndex < _matches.Count)
            {
                _currentMatchIndex = previousMatchIndex;
            }
        }
    }
}
