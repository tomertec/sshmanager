using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handles terminal autocompletion including completion requests, popup management,
/// and input tracking for command history.
/// </summary>
public sealed class TerminalAutocompletionHandler : ITerminalAutocompletionHandler
{
    private readonly IAutocompletionService? _autocompletionService;
    private readonly ILogger<TerminalAutocompletionHandler> _logger;

    // State tracking
    private readonly StringBuilder _currentInputLine = new();
    private int _cursorPosition;
    private bool _isPopupVisible;
    private IReadOnlyList<CompletionItem>? _completionItems;
    private int _selectedIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalAutocompletionHandler"/> class.
    /// </summary>
    /// <param name="autocompletionService">Optional autocompletion service for fetching suggestions.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public TerminalAutocompletionHandler(
        IAutocompletionService? autocompletionService = null,
        ILogger<TerminalAutocompletionHandler>? logger = null)
    {
        _autocompletionService = autocompletionService;
        _logger = logger ?? NullLogger<TerminalAutocompletionHandler>.Instance;
    }

    /// <inheritdoc />
    public bool IsPopupVisible => _isPopupVisible;

    /// <inheritdoc />
    public int SelectedIndex => _selectedIndex;

    /// <inheritdoc />
    public IReadOnlyList<CompletionItem>? Items => _completionItems;

    /// <inheritdoc />
    public string CurrentInputLine => _currentInputLine.ToString();

    /// <inheritdoc />
    public int CursorPosition => _cursorPosition;

    /// <inheritdoc />
    public event EventHandler<CompletionsReceivedEventArgs>? CompletionsReceived;

    /// <inheritdoc />
    public event EventHandler? PopupHidden;

    /// <inheritdoc />
    public event EventHandler<CompletionSelectionChangedEventArgs>? SelectionChanged;

    /// <inheritdoc />
    public async Task RequestCompletionsAsync(IAutocompletionHandlerContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // Get the autocompletion service (must be set via constructor)
            if (_autocompletionService == null)
            {
                _logger.LogDebug("Autocompletion service not available");
                return;
            }

            var currentLine = _currentInputLine.ToString();
            if (string.IsNullOrWhiteSpace(currentLine))
            {
                _logger.LogDebug("Current line is empty, skipping completion request");
                return;
            }

            // Request completions from service
            var completions = await _autocompletionService.GetCompletionsAsync(
                context.ShellStream,
                context.HostId,
                currentLine,
                _cursorPosition,
                ct);

            if (completions.Count > 0)
            {
                _logger.LogDebug("Received {Count} completion suggestions", completions.Count);
                ShowPopup(completions);
            }
            else
            {
                _logger.LogDebug("No completions found for input: {Input}", currentLine);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Completion request was cancelled");
        }
        catch (Exception ex)
        {
            // Log but don't crash - autocompletion is not critical
            _logger.LogWarning(ex, "Autocompletion request failed");
        }
    }

    /// <inheritdoc />
    public void ShowPopup(IReadOnlyList<CompletionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _completionItems = items;
        _selectedIndex = 0;
        _isPopupVisible = true;

        // Raise event for UI to show popup
        CompletionsReceived?.Invoke(this, new CompletionsReceivedEventArgs(items));
        _logger.LogDebug("Completion popup shown with {Count} items", items.Count);
    }

    /// <inheritdoc />
    public void HidePopup()
    {
        if (!_isPopupVisible)
        {
            return;
        }

        _isPopupVisible = false;
        _completionItems = null;
        _selectedIndex = 0;

        PopupHidden?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Completion popup hidden");
    }

    /// <inheritdoc />
    public void SelectPrevious()
    {
        if (_completionItems == null || _completionItems.Count == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex - 1 + _completionItems.Count) % _completionItems.Count;
        SelectionChanged?.Invoke(this, new CompletionSelectionChangedEventArgs(_selectedIndex));
        _logger.LogDebug("Selected previous completion item: index {Index}", _selectedIndex);
    }

    /// <inheritdoc />
    public void SelectNext()
    {
        if (_completionItems == null || _completionItems.Count == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + 1) % _completionItems.Count;
        SelectionChanged?.Invoke(this, new CompletionSelectionChangedEventArgs(_selectedIndex));
        _logger.LogDebug("Selected next completion item: index {Index}", _selectedIndex);
    }

    /// <inheritdoc />
    public void AcceptCompletion(IAutocompletionHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_completionItems == null || _selectedIndex < 0 || _selectedIndex >= _completionItems.Count)
        {
            _logger.LogDebug("No valid completion to accept");
            HidePopup();
            return;
        }

        var selected = _completionItems[_selectedIndex];
        var textToInsert = string.IsNullOrEmpty(selected.InsertText)
            ? selected.DisplayText
            : selected.InsertText;

        _logger.LogDebug("Accepting completion: {Text}", textToInsert);
        InsertCompletion(textToInsert, context);
        HidePopup();
    }

    /// <inheritdoc />
    public void InsertCompletion(string text, IAutocompletionHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogDebug("Completion text is empty, nothing to insert");
            return;
        }

        // Extract the word being completed and replace it
        var currentLine = _currentInputLine.ToString();
        var wordStart = FindWordStart(currentLine, _cursorPosition);
        var wordToReplace = currentLine.Substring(wordStart, _cursorPosition - wordStart);

        // Calculate what needs to be inserted (completion minus already typed part)
        var insertion = text;
        if (text.StartsWith(wordToReplace, StringComparison.Ordinal))
        {
            insertion = text.Substring(wordToReplace.Length);
        }

        // Send the completion text to the terminal
        context.SendText(insertion);

        // Update local tracking
        _currentInputLine.Remove(wordStart, wordToReplace.Length);
        _currentInputLine.Insert(wordStart, text);
        _cursorPosition = wordStart + text.Length;

        _logger.LogDebug("Inserted completion text: {Text}", text);
    }

    /// <inheritdoc />
    public void TrackInput(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        foreach (var c in data)
        {
            if (c == '\r' || c == '\n')
            {
                // Record command in history
                var command = _currentInputLine.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(command))
                {
                    _ = RecordCommandAsync(command);
                }

                _currentInputLine.Clear();
                _cursorPosition = 0;
                HidePopup();
            }
            else if (c == '\b' || c == '\x7f') // Backspace
            {
                if (_currentInputLine.Length > 0 && _cursorPosition > 0)
                {
                    _currentInputLine.Remove(_cursorPosition - 1, 1);
                    _cursorPosition--;
                }
                HidePopup();
            }
            else if (c >= 32) // Printable characters
            {
                _currentInputLine.Insert(_cursorPosition, c);
                _cursorPosition++;
                HidePopup();
            }
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _currentInputLine.Clear();
        _cursorPosition = 0;
        HidePopup();
        _logger.LogDebug("Autocompletion state reset");
    }

    /// <summary>
    /// Finds the start position of the word at the cursor position.
    /// </summary>
    /// <param name="line">The input line text.</param>
    /// <param name="position">The cursor position.</param>
    /// <returns>The start index of the word.</returns>
    private static int FindWordStart(string line, int position)
    {
        if (position <= 0) return 0;

        var i = Math.Min(position - 1, line.Length - 1);
        while (i >= 0 && !char.IsWhiteSpace(line[i]))
        {
            i--;
        }
        return i + 1;
    }

    /// <summary>
    /// Records a command in history for future autocompletion.
    /// </summary>
    /// <param name="command">The command to record.</param>
    private async Task RecordCommandAsync(string command)
    {
        try
        {
            if (_autocompletionService != null)
            {
                // Note: We don't have context here, so pass null for hostId
                // This could be improved by passing context through TrackInput
                await _autocompletionService.RecordCommandAsync(null, command);
                _logger.LogDebug("Recorded command in history: {Command}", command);
            }
        }
        catch (Exception ex)
        {
            // Ignore recording errors - not critical
            _logger.LogDebug(ex, "Failed to record command in history");
        }
    }
}
