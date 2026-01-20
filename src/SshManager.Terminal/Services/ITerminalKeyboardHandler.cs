using System.Windows.Input;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handler interface for terminal keyboard shortcuts.
/// </summary>
public interface ITerminalKeyboardHandler
{
    /// <summary>
    /// Handles a key down event and returns whether it was handled.
    /// </summary>
    /// <param name="e">The key event arguments.</param>
    /// <param name="context">Context providing access to terminal operations.</param>
    /// <returns>True if the key was handled, false otherwise.</returns>
    bool HandleKeyDown(KeyEventArgs e, IKeyboardHandlerContext context);
}

/// <summary>
/// Context interface providing access to terminal operations for keyboard handling.
/// </summary>
public interface IKeyboardHandlerContext
{
    /// <summary>
    /// Sends text directly to the SSH connection.
    /// </summary>
    /// <param name="text">The text to send.</param>
    void SendText(string text);

    /// <summary>
    /// Shows the find overlay for searching terminal output.
    /// </summary>
    void ShowFindOverlay();

    /// <summary>
    /// Hides the find overlay.
    /// </summary>
    void HideFindOverlay();

    /// <summary>
    /// Gets whether the find overlay is currently visible.
    /// </summary>
    bool IsFindOverlayVisible { get; }

    /// <summary>
    /// Copies selected text to the clipboard.
    /// </summary>
    void CopyToClipboard();

    /// <summary>
    /// Pastes text from the clipboard to the terminal.
    /// </summary>
    void PasteFromClipboard();

    /// <summary>
    /// Increases the terminal font size.
    /// </summary>
    void ZoomIn();

    /// <summary>
    /// Decreases the terminal font size.
    /// </summary>
    void ZoomOut();

    /// <summary>
    /// Resets the terminal font size to default.
    /// </summary>
    void ResetZoom();

    /// <summary>
    /// Request autocompletion suggestions for the current input.
    /// </summary>
    void RequestCompletions();

    /// <summary>
    /// Insert the selected completion text at the current cursor position.
    /// </summary>
    void InsertCompletion(string text);

    /// <summary>
    /// Gets the current input line text (text since last newline/Enter).
    /// </summary>
    string GetCurrentInputLine();

    /// <summary>
    /// Gets the cursor position within the current input line.
    /// </summary>
    int GetCursorPosition();

    /// <summary>
    /// Whether the completion popup is currently visible.
    /// </summary>
    bool IsCompletionPopupVisible { get; }

    /// <summary>
    /// Move selection up in the completion popup.
    /// </summary>
    void CompletionSelectPrevious();

    /// <summary>
    /// Move selection down in the completion popup.
    /// </summary>
    void CompletionSelectNext();

    /// <summary>
    /// Accept the currently selected completion.
    /// </summary>
    void AcceptCompletion();

    /// <summary>
    /// Hide the completion popup.
    /// </summary>
    void HideCompletionPopup();
}
