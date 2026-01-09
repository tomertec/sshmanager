namespace SshManager.App.Services;

/// <summary>
/// Service for monitoring Windows session state events (lock/unlock).
/// </summary>
public interface ISessionStateService : IDisposable
{
    /// <summary>
    /// Event raised when the Windows session is locked.
    /// </summary>
    event EventHandler? SessionLocked;

    /// <summary>
    /// Event raised when the Windows session is unlocked.
    /// </summary>
    event EventHandler? SessionUnlocked;

    /// <summary>
    /// Starts monitoring session state events.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops monitoring session state events.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Gets whether the service is currently monitoring session state.
    /// </summary>
    bool IsMonitoring { get; }
}
