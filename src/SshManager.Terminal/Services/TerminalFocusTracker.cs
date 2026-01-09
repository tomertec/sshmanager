using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Centralized service for tracking terminal focus state.
/// Provides reliable focus detection that works correctly with WebView2 controls.
/// </summary>
public sealed class TerminalFocusTracker : ITerminalFocusTracker
{
    private readonly ILogger<TerminalFocusTracker> _logger;
    private readonly object _lock = new();
    private string? _focusedSessionId;

    public TerminalFocusTracker(ILogger<TerminalFocusTracker>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalFocusTracker>.Instance;
    }

    /// <inheritdoc />
    public bool IsAnyTerminalFocused
    {
        get
        {
            lock (_lock)
            {
                return _focusedSessionId != null;
            }
        }
    }

    /// <inheritdoc />
    public string? FocusedSessionId
    {
        get
        {
            lock (_lock)
            {
                return _focusedSessionId;
            }
        }
    }

    /// <inheritdoc />
    public event Action<bool>? FocusChanged;

    /// <inheritdoc />
    public void NotifyFocusGained(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        bool changed;
        lock (_lock)
        {
            changed = _focusedSessionId != sessionId;
            _focusedSessionId = sessionId;
        }

        if (changed)
        {
            _logger.LogDebug("Terminal focus gained: {SessionId}", sessionId);
            FocusChanged?.Invoke(true);
        }
    }

    /// <inheritdoc />
    public void NotifyFocusLost(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        bool changed;
        lock (_lock)
        {
            // Only clear if this session was the focused one
            if (_focusedSessionId == sessionId)
            {
                _focusedSessionId = null;
                changed = true;
            }
            else
            {
                changed = false;
            }
        }

        if (changed)
        {
            _logger.LogDebug("Terminal focus lost: {SessionId}", sessionId);
            FocusChanged?.Invoke(false);
        }
    }
}
