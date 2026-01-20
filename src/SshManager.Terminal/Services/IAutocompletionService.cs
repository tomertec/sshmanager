namespace SshManager.Terminal.Services;

using SshManager.Core.Models;
using Renci.SshNet;

/// <summary>
/// Service for providing terminal autocompletion suggestions.
/// </summary>
public interface IAutocompletionService
{
    /// <summary>
    /// Gets completion suggestions for the current input line.
    /// </summary>
    /// <param name="shellStream">The SSH shell stream for remote completions (can be null for local-only).</param>
    /// <param name="hostId">The host ID for history lookups (can be null for general history).</param>
    /// <param name="currentLine">The current input line text.</param>
    /// <param name="cursorPosition">The cursor position within the line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of completion suggestions sorted by relevance.</returns>
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        ShellStream? shellStream,
        Guid? hostId,
        string currentLine,
        int cursorPosition,
        CancellationToken ct = default);

    /// <summary>
    /// Records a command in history for future suggestions.
    /// </summary>
    /// <param name="hostId">The host where the command was executed.</param>
    /// <param name="command">The command text.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordCommandAsync(Guid? hostId, string command, CancellationToken ct = default);
}
