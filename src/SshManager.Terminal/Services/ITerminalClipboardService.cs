using System.Windows;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service interface for terminal clipboard operations.
/// </summary>
public interface ITerminalClipboardService
{
    /// <summary>
    /// Copies selected text from the terminal to the clipboard.
    /// </summary>
    void CopyToClipboard();

    /// <summary>
    /// Pastes text from the clipboard to the terminal.
    /// </summary>
    /// <param name="sendText">Action to send the pasted text to the terminal.</param>
    void PasteFromClipboard(Action<string> sendText);

    /// <summary>
    /// Gets whether the clipboard contains text that can be pasted.
    /// </summary>
    bool HasClipboardText { get; }
}
