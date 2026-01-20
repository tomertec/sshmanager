using System;
using System.Threading.Tasks;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Configuration for exponential backoff retry behavior.
/// </summary>
public sealed class ExponentialBackoffConfig
{
    /// <summary>
    /// Base delay for reconnection attempts.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between reconnection attempts.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Jitter factor for randomizing delays (0.0 to 1.0).
    /// Adds randomness to prevent reconnection storms when multiple clients reconnect.
    /// Default: 0.5 (delay varies from 0.5x to 1.5x).
    /// </summary>
    public double JitterFactor { get; set; } = 0.5;

    /// <summary>
    /// Whether to use exponential backoff. If false, uses fixed BaseDelay.
    /// Default: true.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Creates default configuration.
    /// </summary>
    public static ExponentialBackoffConfig Default => new();
}

/// <summary>
/// Interface for managing automatic reconnection logic for terminal sessions.
/// Handles reconnection attempts with configurable delays, exponential backoff,
/// and maximum attempt limits.
/// </summary>
public interface IAutoReconnectManager
{
    /// <summary>
    /// Gets or sets whether auto-reconnect is enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts before giving up.
    /// </summary>
    int MaxAttempts { get; set; }

    /// <summary>
    /// Gets the current number of reconnection attempts made since the last successful connection.
    /// </summary>
    int AttemptCount { get; }

    /// <summary>
    /// Gets whether a reconnection attempt is currently in progress.
    /// </summary>
    bool IsReconnecting { get; }

    /// <summary>
    /// Gets whether reconnection is possible for the current session.
    /// </summary>
    bool CanReconnect { get; }

    /// <summary>
    /// Gets whether reconnection is currently paused (e.g., waiting for network).
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Gets the delay that will be used for the next reconnection attempt.
    /// </summary>
    TimeSpan NextDelay { get; }

    /// <summary>
    /// Event raised when reconnection succeeds after one or more attempts.
    /// </summary>
    event EventHandler? ReconnectSucceeded;

    /// <summary>
    /// Event raised when all reconnection attempts have been exhausted.
    /// </summary>
    event EventHandler? ReconnectExhausted;

    /// <summary>
    /// Event raised when reconnection is paused (e.g., network unavailable).
    /// </summary>
    event EventHandler? ReconnectPaused;

    /// <summary>
    /// Event raised when reconnection is resumed (e.g., network restored).
    /// </summary>
    event EventHandler? ReconnectResumed;

    /// <summary>
    /// Configures auto-reconnect behavior.
    /// </summary>
    /// <param name="enabled">Whether auto-reconnect is enabled.</param>
    /// <param name="maxAttempts">Maximum number of reconnection attempts (default 3).</param>
    /// <param name="delay">Delay between reconnection attempts (default 2 seconds). Deprecated: use ConfigureBackoff instead.</param>
    void Configure(bool enabled, int maxAttempts = 3, TimeSpan? delay = null);

    /// <summary>
    /// Configures exponential backoff behavior for reconnection attempts.
    /// </summary>
    /// <param name="config">The backoff configuration.</param>
    void ConfigureBackoff(ExponentialBackoffConfig config);

    /// <summary>
    /// Resets the reconnection attempt counter to zero.
    /// Call this after a successful connection.
    /// </summary>
    void ResetAttempts();

    /// <summary>
    /// Pauses reconnection attempts (e.g., when network is unavailable).
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes reconnection attempts after being paused.
    /// </summary>
    void Resume();

    /// <summary>
    /// Handles disconnection event and initiates auto-reconnect if enabled.
    /// </summary>
    /// <param name="context">Reconnection context with session and service information.</param>
    /// <returns>Task representing the async operation.</returns>
    Task HandleDisconnectionAsync(IReconnectContext context);

    /// <summary>
    /// Manually triggers a reconnection attempt using the provided context.
    /// </summary>
    /// <param name="context">Reconnection context with session and service information.</param>
    /// <returns>Result of the reconnection attempt.</returns>
    Task<ReconnectResult> ReconnectAsync(IReconnectContext context);
}

/// <summary>
/// Context information required for reconnection attempts.
/// Provides access to session data and reconnection capabilities.
/// </summary>
public interface IReconnectContext
{
    /// <summary>
    /// Gets the terminal session to reconnect.
    /// </summary>
    TerminalSession? Session { get; }

    /// <summary>
    /// Gets the serial connection service (if this is a serial session).
    /// </summary>
    ISerialConnectionService? SerialService { get; }

    /// <summary>
    /// Gets the serial connection info (if this is a serial session).
    /// </summary>
    SerialConnectionInfo? SerialConnectionInfo { get; }

    /// <summary>
    /// Attempts to reconnect a serial connection.
    /// </summary>
    /// <returns>Task representing the async reconnection operation.</returns>
    Task ReconnectSerialAsync();

    /// <summary>
    /// Shows a status message to the user.
    /// </summary>
    /// <param name="message">The status message to display.</param>
    void ShowStatus(string message);
}

/// <summary>
/// Result of a reconnection attempt.
/// </summary>
/// <param name="Success">True if reconnection succeeded, false otherwise.</param>
/// <param name="ErrorMessage">Error message if reconnection failed, null otherwise.</param>
public record ReconnectResult(bool Success, string? ErrorMessage = null);
