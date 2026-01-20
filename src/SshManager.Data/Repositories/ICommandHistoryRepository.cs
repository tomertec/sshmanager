using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository interface for managing command history.
/// </summary>
public interface ICommandHistoryRepository
{
    /// <summary>
    /// Gets command suggestions matching the specified prefix.
    /// </summary>
    /// <param name="hostId">Optional host ID to filter commands. If null, searches across all hosts.</param>
    /// <param name="prefix">The command prefix to search for.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of matching commands ordered by use count (descending) and last execution (descending).</returns>
    Task<List<CommandHistoryEntry>> GetSuggestionsAsync(
        Guid? hostId,
        string prefix,
        int maxResults,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a command to history or increments use count if it already exists.
    /// </summary>
    /// <param name="hostId">Optional host ID to associate the command with.</param>
    /// <param name="command">The command to add.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(Guid? hostId, string command, CancellationToken ct = default);

    /// <summary>
    /// Gets the most recent commands.
    /// </summary>
    /// <param name="hostId">Optional host ID to filter commands. If null, gets from all hosts.</param>
    /// <param name="count">Number of commands to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of recent commands ordered by execution time (descending).</returns>
    Task<List<CommandHistoryEntry>> GetRecentAsync(
        Guid? hostId,
        int count,
        CancellationToken ct = default);

    /// <summary>
    /// Clears all command history for a specific host.
    /// </summary>
    /// <param name="hostId">The host ID whose history to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearHostHistoryAsync(Guid hostId, CancellationToken ct = default);

    /// <summary>
    /// Clears all command history across all hosts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ClearAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the most frequently used commands.
    /// </summary>
    /// <param name="hostId">Optional host ID to filter commands. If null, gets from all hosts.</param>
    /// <param name="count">Number of commands to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of commands ordered by use count (descending).</returns>
    Task<List<CommandHistoryEntry>> GetMostUsedAsync(
        Guid? hostId,
        int count,
        CancellationToken ct = default);
}
