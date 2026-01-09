namespace SshManager.App.Services;

/// <summary>
/// Provides health status information for background services.
/// Implement this interface on hosted services to expose their operational status.
/// </summary>
public interface IBackgroundServiceHealth
{
    /// <summary>
    /// Gets the name of the background service.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Gets whether the service is currently healthy and operational.
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Gets the current status message describing the service state.
    /// </summary>
    string StatusMessage { get; }

    /// <summary>
    /// Gets the last error message if the service encountered an error, null otherwise.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Gets the timestamp of the last successful operation.
    /// </summary>
    DateTimeOffset? LastSuccessfulRun { get; }

    /// <summary>
    /// Gets the timestamp of the last error, if any.
    /// </summary>
    DateTimeOffset? LastErrorTime { get; }

    /// <summary>
    /// Gets the number of consecutive failures since the last success.
    /// </summary>
    int ConsecutiveFailures { get; }

    /// <summary>
    /// Gets additional health metrics specific to the service.
    /// </summary>
    IReadOnlyDictionary<string, object> Metrics { get; }
}

/// <summary>
/// Base class for background services that provides health tracking functionality.
/// </summary>
public abstract class HealthTrackingBackgroundService : IBackgroundServiceHealth
{
    private readonly object _healthLock = new();
    private bool _isHealthy = true;
    private string _statusMessage = "Starting...";
    private string? _lastError;
    private DateTimeOffset? _lastSuccessfulRun;
    private DateTimeOffset? _lastErrorTime;
    private int _consecutiveFailures;
    private readonly Dictionary<string, object> _metrics = new();

    /// <inheritdoc />
    public abstract string ServiceName { get; }

    /// <inheritdoc />
    public bool IsHealthy
    {
        get { lock (_healthLock) return _isHealthy; }
    }

    /// <inheritdoc />
    public string StatusMessage
    {
        get { lock (_healthLock) return _statusMessage; }
    }

    /// <inheritdoc />
    public string? LastError
    {
        get { lock (_healthLock) return _lastError; }
    }

    /// <inheritdoc />
    public DateTimeOffset? LastSuccessfulRun
    {
        get { lock (_healthLock) return _lastSuccessfulRun; }
    }

    /// <inheritdoc />
    public DateTimeOffset? LastErrorTime
    {
        get { lock (_healthLock) return _lastErrorTime; }
    }

    /// <inheritdoc />
    public int ConsecutiveFailures
    {
        get { lock (_healthLock) return _consecutiveFailures; }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Metrics
    {
        get
        {
            lock (_healthLock)
            {
                return new Dictionary<string, object>(_metrics);
            }
        }
    }

    /// <summary>
    /// Records a successful operation.
    /// </summary>
    /// <param name="statusMessage">Optional status message.</param>
    protected void RecordSuccess(string? statusMessage = null)
    {
        lock (_healthLock)
        {
            _isHealthy = true;
            _lastSuccessfulRun = DateTimeOffset.UtcNow;
            _consecutiveFailures = 0;
            _lastError = null;
            if (statusMessage != null)
            {
                _statusMessage = statusMessage;
            }
        }
    }

    /// <summary>
    /// Records a failed operation.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="markUnhealthy">Whether to mark the service as unhealthy (default: only after 3 consecutive failures).</param>
    protected void RecordFailure(string error, bool? markUnhealthy = null)
    {
        lock (_healthLock)
        {
            _consecutiveFailures++;
            _lastError = error;
            _lastErrorTime = DateTimeOffset.UtcNow;
            _statusMessage = $"Error: {error}";

            // Mark unhealthy after 3 consecutive failures by default
            if (markUnhealthy ?? _consecutiveFailures >= 3)
            {
                _isHealthy = false;
            }
        }
    }

    /// <summary>
    /// Updates the current status message without changing health state.
    /// </summary>
    /// <param name="message">The status message.</param>
    protected void UpdateStatus(string message)
    {
        lock (_healthLock)
        {
            _statusMessage = message;
        }
    }

    /// <summary>
    /// Sets a custom metric value.
    /// </summary>
    /// <param name="key">The metric key.</param>
    /// <param name="value">The metric value.</param>
    protected void SetMetric(string key, object value)
    {
        lock (_healthLock)
        {
            _metrics[key] = value;
        }
    }

    /// <summary>
    /// Removes a custom metric.
    /// </summary>
    /// <param name="key">The metric key to remove.</param>
    protected void RemoveMetric(string key)
    {
        lock (_healthLock)
        {
            _metrics.Remove(key);
        }
    }
}

/// <summary>
/// Aggregates health status from all registered background services.
/// </summary>
public interface IBackgroundServiceHealthAggregator
{
    /// <summary>
    /// Gets the overall health status (true if all services are healthy).
    /// </summary>
    bool IsOverallHealthy { get; }

    /// <summary>
    /// Gets all registered health providers.
    /// </summary>
    IReadOnlyList<IBackgroundServiceHealth> Services { get; }

    /// <summary>
    /// Gets a summary of unhealthy services.
    /// </summary>
    IReadOnlyList<(string ServiceName, string Error)> UnhealthyServices { get; }
}

/// <summary>
/// Implementation of the health aggregator that collects status from all registered services.
/// </summary>
public sealed class BackgroundServiceHealthAggregator : IBackgroundServiceHealthAggregator
{
    private readonly IEnumerable<IBackgroundServiceHealth> _healthProviders;

    public BackgroundServiceHealthAggregator(IEnumerable<IBackgroundServiceHealth> healthProviders)
    {
        _healthProviders = healthProviders;
    }

    /// <inheritdoc />
    public bool IsOverallHealthy => _healthProviders.All(p => p.IsHealthy);

    /// <inheritdoc />
    public IReadOnlyList<IBackgroundServiceHealth> Services => _healthProviders.ToList();

    /// <inheritdoc />
    public IReadOnlyList<(string ServiceName, string Error)> UnhealthyServices =>
        _healthProviders
            .Where(p => !p.IsHealthy)
            .Select(p => (p.ServiceName, p.LastError ?? "Unknown error"))
            .ToList();
}
