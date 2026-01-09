using System.Text;
using System.Text.RegularExpressions;

namespace SshManager.Terminal;

/// <summary>
/// Stores terminal output text for search and logging functionality.
/// This is a simple text-based buffer that captures output from the terminal
/// and strips ANSI escape sequences for searchable plain text storage.
/// </summary>
public sealed class TerminalOutputBuffer
{
    private readonly List<string> _lines = new();
    private readonly StringBuilder _currentLine = new();
    private readonly object _lock = new();
    private int _maxLines;

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
    /// <param name="maxLines">Maximum number of lines to retain.</param>
    public TerminalOutputBuffer(int maxLines = 10000)
    {
        _maxLines = Math.Max(100, maxLines);
    }

    /// <summary>
    /// Gets or sets the maximum number of lines to retain.
    /// </summary>
    public int MaxLines
    {
        get => _maxLines;
        set => _maxLines = Math.Max(100, value);
    }

    /// <summary>
    /// Gets the current number of lines in the buffer.
    /// </summary>
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

    /// <summary>
    /// Appends text output from the terminal.
    /// ANSI escape sequences are stripped and the text is stored as plain text.
    /// </summary>
    /// <param name="text">The terminal output text (may contain ANSI sequences).</param>
    public void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_lock)
        {
            // Strip ANSI escape sequences
            var cleanText = StripAnsiEscapes(text);

            foreach (var ch in cleanText)
            {
                if (ch == '\n')
                {
                    // End of line - store it
                    _lines.Add(_currentLine.ToString());
                    _currentLine.Clear();

                    // Trim excess lines
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
    /// Gets a specific line by index.
    /// </summary>
    /// <param name="index">The line index (0 = oldest line).</param>
    /// <returns>The line text, or empty string if index is out of range.</returns>
    public string GetLine(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _lines.Count)
            {
                return _lines[index];
            }
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets a range of lines from the buffer.
    /// </summary>
    /// <param name="startIndex">Start index (0 = oldest line).</param>
    /// <param name="count">Number of lines to retrieve.</param>
    /// <returns>The requested lines.</returns>
    public IReadOnlyList<string> GetLines(int startIndex, int count)
    {
        lock (_lock)
        {
            var result = new List<string>();
            var end = Math.Min(startIndex + count, _lines.Count);

            for (var i = startIndex; i < end; i++)
            {
                if (i >= 0 && i < _lines.Count)
                {
                    result.Add(_lines[i]);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Gets all lines as a single string.
    /// </summary>
    public string GetAllText()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var line in _lines)
            {
                sb.AppendLine(line);
            }
            if (_currentLine.Length > 0)
            {
                sb.Append(_currentLine);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Clears all lines from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
            _currentLine.Clear();
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
    /// Trims excess lines from the beginning of the buffer.
    /// </summary>
    private void TrimExcess()
    {
        var excess = _lines.Count - _maxLines;
        if (excess > 0)
        {
            _lines.RemoveRange(0, excess);
        }
    }
}
