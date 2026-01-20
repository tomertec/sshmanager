using SshManager.Core.Models;
using Renci.SshNet;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handler interface for terminal autocompletion functionality.
/// Manages completion requests, popup state, and user interactions with completion suggestions.
/// </summary>
public interface ITerminalAutocompletionHandler
{
    /// <summary>
    /// Gets whether the completion popup is currently visible.
    /// </summary>
    bool IsPopupVisible { get; }

    /// <summary>
    /// Gets the currently selected completion index.
    /// </summary>
    int SelectedIndex { get; }

    /// <summary>
    /// Gets the current completion items displayed in the popup.
    /// </summary>
    IReadOnlyList<CompletionItem>? Items { get; }

    /// <summary>
    /// Gets the current input line text.
    /// </summary>
    string CurrentInputLine { get; }

    /// <summary>
    /// Gets the cursor position within the current input line.
    /// </summary>
    int CursorPosition { get; }

    /// <summary>
    /// Requests completions for the current input line from the autocompletion service.
    /// </summary>
    /// <param name="context">Context providing access to terminal connection and operations.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RequestCompletionsAsync(IAutocompletionHandlerContext context, CancellationToken ct = default);

    /// <summary>
    /// Shows the completion popup with the given items.
    /// </summary>
    /// <param name="items">The completion items to display.</param>
    void ShowPopup(IReadOnlyList<CompletionItem> items);

    /// <summary>
    /// Hides the completion popup.
    /// </summary>
    void HidePopup();

    /// <summary>
    /// Selects the previous item in the completion popup.
    /// </summary>
    void SelectPrevious();

    /// <summary>
    /// Selects the next item in the completion popup.
    /// </summary>
    void SelectNext();

    /// <summary>
    /// Accepts the currently selected completion and inserts it.
    /// </summary>
    /// <param name="context">Context providing access to terminal operations.</param>
    void AcceptCompletion(IAutocompletionHandlerContext context);

    /// <summary>
    /// Inserts completion text at the current cursor position.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    /// <param name="context">Context providing access to terminal operations.</param>
    void InsertCompletion(string text, IAutocompletionHandlerContext context);

    /// <summary>
    /// Tracks input characters for autocompletion and history.
    /// </summary>
    /// <param name="data">The input data to track.</param>
    void TrackInput(string data);

    /// <summary>
    /// Resets the autocompletion state (clears input tracking and hides popup).
    /// </summary>
    void Reset();

    /// <summary>
    /// Event raised when completion suggestions are received.
    /// </summary>
    event EventHandler<CompletionsReceivedEventArgs>? CompletionsReceived;

    /// <summary>
    /// Event raised when the completion popup is hidden.
    /// </summary>
    event EventHandler? PopupHidden;

    /// <summary>
    /// Event raised when the selected completion changes.
    /// </summary>
    event EventHandler<CompletionSelectionChangedEventArgs>? SelectionChanged;
}

/// <summary>
/// Context interface providing access to terminal operations for autocompletion handling.
/// </summary>
public interface IAutocompletionHandlerContext
{
    /// <summary>
    /// Gets the SSH shell stream for remote completions (null for serial or non-SSH connections).
    /// </summary>
    ShellStream? ShellStream { get; }

    /// <summary>
    /// Gets the host ID for history lookups (null if no host context).
    /// </summary>
    Guid? HostId { get; }

    /// <summary>
    /// Sends text directly to the connection (SSH or serial).
    /// </summary>
    /// <param name="text">The text to send.</param>
    void SendText(string text);
}

/// <summary>
/// Event arguments for completion suggestions received.
/// </summary>
public sealed record CompletionsReceivedEventArgs(IReadOnlyList<CompletionItem> Items);

/// <summary>
/// Event arguments for completion selection changed.
/// </summary>
public sealed record CompletionSelectionChangedEventArgs(int SelectedIndex);
