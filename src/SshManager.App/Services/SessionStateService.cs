using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace SshManager.App.Services;

/// <summary>
/// Service for monitoring Windows session state events (lock/unlock).
/// Uses the SystemEvents.SessionSwitch event to detect session state changes.
/// </summary>
public sealed class SessionStateService : ISessionStateService
{
    private readonly ILogger<SessionStateService> _logger;
    private bool _isMonitoring;
    private bool _disposed;

    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;

    public bool IsMonitoring => _isMonitoring;

    public SessionStateService(ILogger<SessionStateService>? logger = null)
    {
        _logger = logger ?? NullLogger<SessionStateService>.Instance;
    }

    /// <inheritdoc />
    public void StartMonitoring()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isMonitoring)
        {
            _logger.LogDebug("Session state monitoring already active");
            return;
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _isMonitoring = true;
        _logger.LogInformation("Started monitoring Windows session state events");
    }

    /// <inheritdoc />
    public void StopMonitoring()
    {
        if (!_isMonitoring)
            return;

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _isMonitoring = false;
        _logger.LogInformation("Stopped monitoring Windows session state events");
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _logger.LogDebug("Session switch event: {Reason}", e.Reason);

        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                _logger.LogInformation("Windows session locked");
                SessionLocked?.Invoke(this, EventArgs.Empty);
                break;

            case SessionSwitchReason.SessionUnlock:
                _logger.LogInformation("Windows session unlocked");
                SessionUnlocked?.Invoke(this, EventArgs.Empty);
                break;

            case SessionSwitchReason.SessionLogoff:
                _logger.LogInformation("Windows session logoff detected");
                SessionLocked?.Invoke(this, EventArgs.Empty);
                break;

            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                _logger.LogDebug("Session disconnected (Reason: {Reason})", e.Reason);
                SessionLocked?.Invoke(this, EventArgs.Empty);
                break;

            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                _logger.LogDebug("Session connected (Reason: {Reason})", e.Reason);
                SessionUnlocked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        StopMonitoring();
        _disposed = true;
        _logger.LogDebug("SessionStateService disposed");
    }
}
