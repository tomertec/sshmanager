using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository for managing saved sessions for crash recovery.
/// </summary>
public interface ISavedSessionRepository
{
    /// <summary>
    /// Gets all saved sessions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of saved sessions.</returns>
    Task<List<SavedSession>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets saved sessions that were not from a graceful shutdown.
    /// These are candidates for recovery.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of sessions to potentially recover.</returns>
    Task<List<SavedSession>> GetRecoverableSessionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves a session for potential recovery.
    /// </summary>
    /// <param name="session">The session to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(SavedSession session, CancellationToken ct = default);

    /// <summary>
    /// Saves multiple sessions.
    /// </summary>
    /// <param name="sessions">The sessions to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAllAsync(IEnumerable<SavedSession> sessions, CancellationToken ct = default);

    /// <summary>
    /// Marks a saved session as gracefully shutdown.
    /// </summary>
    /// <param name="id">The session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkAsGracefulAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Marks all saved sessions as gracefully shutdown.
    /// Called on normal application exit.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task MarkAllAsGracefulAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes a saved session.
    /// </summary>
    /// <param name="id">The session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Deletes all saved sessions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ClearAllAsync(CancellationToken ct = default);
}
