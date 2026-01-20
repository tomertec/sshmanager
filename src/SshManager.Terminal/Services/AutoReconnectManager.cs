using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for managing automatic reconnection logic for terminal sessions.
/// Implements exponential backoff with jitter and attempt limiting to prevent connection storms.
/// </summary>
/// <remarks>
/// This service was extracted from SshTerminalControl to reduce complexity and improve testability.
/// It handles the reconnection state machine: attempt counting, delay handling, and success/failure events.
/// Supports exponential backoff with configurable jitter to prevent thundering herd problems.
/// </remarks>
public sealed class AutoReconnectManager : IAutoReconnectManager, INotifyPropertyChanged
{
    private readonly ILogger<AutoReconnectManager> _logger;
    private readonly Random _random = new();

    private bool _enabled;
    private int _maxAttempts = 3;
    private int _attemptCount;
    private TimeSpan _delay = TimeSpan.FromSeconds(2);
    private bool _isReconnecting;
    private bool _isPaused;
    private ExponentialBackoffConfig _backoffConfig = ExponentialBackoffConfig.Default;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Event raised when reconnection succeeds after one or more attempts.
    /// </summary>
    public event EventHandler? ReconnectSucceeded;

    /// <summary>
    /// Event raised when all reconnection attempts have been exhausted.
    /// </summary>
    public event EventHandler? ReconnectExhausted;

    /// <summary>
    /// Event raised when reconnection is paused (e.g., network unavailable).
    /// </summary>
    public event EventHandler? ReconnectPaused;

    /// <summary>
    /// Event raised when reconnection is resumed (e.g., network restored).
    /// </summary>
    public event EventHandler? ReconnectResumed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoReconnectManager"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public AutoReconnectManager(ILogger<AutoReconnectManager>? logger = null)
    {
        _logger = logger ?? NullLogger<AutoReconnectManager>.Instance;
    }

    /// <summary>
    /// Gets or sets whether auto-reconnect is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                OnPropertyChanged();
                _logger.LogDebug("Auto-reconnect {State}", value ? "enabled" : "disabled");
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts before giving up.
    /// </summary>
    public int MaxAttempts
    {
        get => _maxAttempts;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Must be non-negative");
            if (_maxAttempts != value)
            {
                _maxAttempts = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the current number of reconnection attempts made since the last successful connection.
    /// </summary>
    public int AttemptCount => _attemptCount;

    /// <summary>
    /// Gets whether a reconnection attempt is currently in progress.
    /// </summary>
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// Gets whether reconnection is possible for the current session.
    /// </summary>
    public bool CanReconnect => _enabled && !_isReconnecting && !_isPaused && _attemptCount < _maxAttempts;

    /// <summary>
    /// Gets whether reconnection is currently paused (e.g., waiting for network).
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Gets the delay that will be used for the next reconnection attempt.
    /// </summary>
    public TimeSpan NextDelay => CalculateDelay(_attemptCount);

    /// <summary>
    /// Configures auto-reconnect behavior.
    /// </summary>
    /// <param name="enabled">Whether auto-reconnect is enabled.</param>
    /// <param name="maxAttempts">Maximum number of reconnection attempts (default 3).</param>
    /// <param name="delay">Delay between reconnection attempts (default 2 seconds).</param>
    public void Configure(bool enabled, int maxAttempts = 3, TimeSpan? delay = null)
    {
        if (maxAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be non-negative");
        }

        if (delay.HasValue && delay.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Must be non-negative");
        }

        var changed = false;

        if (_enabled != enabled)
        {
            _enabled = enabled;
            OnPropertyChanged(nameof(IsEnabled));
            changed = true;
        }

        if (_maxAttempts != maxAttempts)
        {
            _maxAttempts = maxAttempts;
            OnPropertyChanged(nameof(MaxAttempts));
            changed = true;
        }

        if (delay.HasValue && _delay != delay.Value)
        {
            _delay = delay.Value;
            changed = true;
        }

        if (changed)
        {
            _logger.LogDebug("Auto-reconnect configured: enabled={Enabled}, maxAttempts={MaxAttempts}, delay={Delay}",
                enabled, maxAttempts, _delay);
        }
    }

    /// <summary>
    /// Configures exponential backoff behavior for reconnection attempts.
    /// </summary>
    /// <param name="config">The backoff configuration.</param>
    public void ConfigureBackoff(ExponentialBackoffConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _backoffConfig = config;
        _logger.LogDebug(
            "Backoff configured: baseDelay={BaseDelay}, maxDelay={MaxDelay}, jitter={Jitter}, exponential={Exponential}",
            config.BaseDelay, config.MaxDelay, config.JitterFactor, config.UseExponentialBackoff);
    }

    /// <summary>
    /// Pauses reconnection attempts (e.g., when network is unavailable).
    /// </summary>
    public void Pause()
    {
        if (_isPaused)
        {
            return;
        }

        _isPaused = true;
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanReconnect));

        _logger.LogInformation("Reconnection attempts paused");
        ReconnectPaused?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resumes reconnection attempts after being paused.
    /// </summary>
    public void Resume()
    {
        if (!_isPaused)
        {
            return;
        }

        _isPaused = false;
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanReconnect));

        _logger.LogInformation("Reconnection attempts resumed");
        ReconnectResumed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets the reconnection attempt counter to zero.
    /// Call this after a successful connection.
    /// </summary>
    public void ResetAttempts()
    {
        if (_attemptCount != 0)
        {
            _attemptCount = 0;
            OnPropertyChanged(nameof(AttemptCount));
            OnPropertyChanged(nameof(CanReconnect));
            _logger.LogDebug("Reconnection attempt counter reset");
        }
    }

    /// <summary>
    /// Handles disconnection event and initiates auto-reconnect if enabled.
    /// </summary>
    /// <param name="context">Reconnection context with session and service information.</param>
    /// <returns>Task representing the async operation.</returns>
    public async Task HandleDisconnectionAsync(IReconnectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Check if auto-reconnect is enabled and we haven't exceeded max attempts
        if (!_enabled || _isReconnecting || _isPaused || _attemptCount >= _maxAttempts)
        {
            _logger.LogDebug(
                "Auto-reconnect not triggered: enabled={Enabled}, isReconnecting={IsReconnecting}, isPaused={IsPaused}, attemptCount={AttemptCount}/{MaxAttempts}",
                _enabled, _isReconnecting, _isPaused, _attemptCount, _maxAttempts);
            return;
        }

        _attemptCount++;
        OnPropertyChanged(nameof(AttemptCount));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(NextDelay));

        // Calculate delay with exponential backoff and jitter
        var delay = CalculateDelay(_attemptCount - 1);

        _logger.LogInformation(
            "Auto-reconnecting (attempt {Attempt}/{Max}) after {Delay:F1}s delay",
            _attemptCount, _maxAttempts, delay.TotalSeconds);

        context.ShowStatus($"Reconnecting in {delay.TotalSeconds:F0}s (attempt {_attemptCount}/{_maxAttempts})...");

        // Wait before attempting reconnection
        await Task.Delay(delay);

        // Check if we got paused during the delay
        if (_isPaused)
        {
            _logger.LogDebug("Reconnection paused during delay, aborting attempt");
            return;
        }

        // Attempt reconnection
        var result = await ReconnectAsync(context);

        if (!result.Success)
        {
            _logger.LogWarning("Auto-reconnect attempt {Attempt} failed: {Error}",
                _attemptCount, result.ErrorMessage ?? "Unknown error");

            // If we've exhausted all attempts, notify listeners
            if (_attemptCount >= _maxAttempts)
            {
                context.ShowStatus($"Reconnection failed after {_maxAttempts} attempts");
                ReconnectExhausted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // Recursively try again if we have attempts remaining
                await HandleDisconnectionAsync(context);
            }
        }
    }

    /// <summary>
    /// Manually triggers a reconnection attempt using the provided context.
    /// </summary>
    /// <param name="context">Reconnection context with session and service information.</param>
    /// <returns>Result of the reconnection attempt.</returns>
    public async Task<ReconnectResult> ReconnectAsync(IReconnectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Session == null)
        {
            return new ReconnectResult(false, "No session available");
        }

        // Validate serial connection prerequisites
        if (context.SerialService == null || context.SerialConnectionInfo == null)
        {
            _logger.LogWarning("Cannot auto-reconnect: missing service or connection info");
            return new ReconnectResult(false, "Missing service or connection info");
        }

        _isReconnecting = true;
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(CanReconnect));

        try
        {
            context.ShowStatus("Reconnecting...");

            // Delegate actual reconnection to the context
            await context.ReconnectSerialAsync();

            // Success - reset attempts
            _attemptCount = 0;
            OnPropertyChanged(nameof(AttemptCount));
            OnPropertyChanged(nameof(CanReconnect));

            _logger.LogInformation("Reconnection successful");

            // Raise success event
            ReconnectSucceeded?.Invoke(this, EventArgs.Empty);

            return new ReconnectResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconnection attempt failed");
            return new ReconnectResult(false, ex.Message);
        }
        finally
        {
            _isReconnecting = false;
            OnPropertyChanged(nameof(IsReconnecting));
            OnPropertyChanged(nameof(CanReconnect));
        }
    }

    /// <summary>
    /// Calculates the delay for a reconnection attempt using exponential backoff with jitter.
    /// </summary>
    /// <param name="attemptNumber">The zero-based attempt number.</param>
    /// <returns>The calculated delay.</returns>
    private TimeSpan CalculateDelay(int attemptNumber)
    {
        TimeSpan baseDelay;

        if (_backoffConfig.UseExponentialBackoff)
        {
            // Exponential backoff: delay = baseDelay * 2^attemptNumber
            var exponentialMs = _backoffConfig.BaseDelay.TotalMilliseconds * Math.Pow(2, attemptNumber);
            baseDelay = TimeSpan.FromMilliseconds(exponentialMs);
        }
        else
        {
            // Fixed delay
            baseDelay = _backoffConfig.BaseDelay;
        }

        // Cap at maximum delay
        if (baseDelay > _backoffConfig.MaxDelay)
        {
            baseDelay = _backoffConfig.MaxDelay;
        }

        // Add jitter to prevent thundering herd
        // Jitter factor of 0.5 means delay varies from 0.5x to 1.5x
        if (_backoffConfig.JitterFactor > 0)
        {
            var jitterMultiplier = 1.0 + (_random.NextDouble() - 0.5) * _backoffConfig.JitterFactor * 2;
            baseDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * jitterMultiplier);
        }

        // Ensure we don't go below zero or above max
        if (baseDelay < TimeSpan.Zero)
        {
            baseDelay = TimeSpan.Zero;
        }
        if (baseDelay > _backoffConfig.MaxDelay)
        {
            baseDelay = _backoffConfig.MaxDelay;
        }

        return baseDelay;
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
