namespace SshManager.Core.Models;

/// <summary>
/// Represents a saved terminal session for crash recovery.
/// Sessions are saved on shutdown and restored if the application crashed.
/// </summary>
public class SavedSession
{
    /// <summary>
    /// Gets or sets the unique identifier for this saved session.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the ID of the host entry associated with this session.
    /// </summary>
    public Guid HostEntryId { get; set; }

    /// <summary>
    /// Gets or sets the session title at the time it was saved.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the session was originally created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when the session state was last saved.
    /// </summary>
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets whether the shutdown was graceful.
    /// If false, the application may have crashed and recovery should be offered.
    /// </summary>
    public bool WasGracefulShutdown { get; set; }
}
