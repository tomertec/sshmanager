using System.IO;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using Renci.SshNet.Common;

namespace SshManager.Terminal.Services;

/// <summary>
/// Configuration options for connection retry behavior.
/// </summary>
public sealed class ConnectionRetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts. Default is 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Initial delay before first retry. Default is 1 second.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retries. Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to use exponential backoff. Default is true.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Jitter factor for randomizing delays (0.0 to 1.0). Default is 0.2 (20%).
    /// </summary>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>
    /// Whether retry is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Creates default options with no retry (single attempt).
    /// </summary>
    public static ConnectionRetryOptions NoRetry => new() { Enabled = false, MaxRetryAttempts = 0 };

    /// <summary>
    /// Creates options for aggressive retry (more attempts, shorter delays).
    /// Useful for unreliable networks.
    /// </summary>
    public static ConnectionRetryOptions Aggressive => new()
    {
        MaxRetryAttempts = 5,
        InitialDelay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(15),
        UseExponentialBackoff = true,
        JitterFactor = 0.3
    };
}

/// <summary>
/// Provides retry policies for SSH/SFTP connection operations.
/// Uses Polly for resilient connection handling with exponential backoff.
/// </summary>
public sealed class ConnectionRetryPolicy : IConnectionRetryPolicy
{
    private readonly ILogger<ConnectionRetryPolicy> _logger;
    private readonly ConnectionRetryOptions _defaultOptions;

    public ConnectionRetryPolicy(
        ILogger<ConnectionRetryPolicy>? logger = null,
        ConnectionRetryOptions? defaultOptions = null)
    {
        _logger = logger ?? NullLogger<ConnectionRetryPolicy>.Instance;
        _defaultOptions = defaultOptions ?? new ConnectionRetryOptions();
    }

    /// <summary>
    /// Executes an async operation with retry policy.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="operationName">Name for logging purposes.</param>
    /// <param name="options">Optional retry options (uses defaults if null).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        ConnectionRetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var retryOptions = options ?? _defaultOptions;

        if (!retryOptions.Enabled || retryOptions.MaxRetryAttempts <= 0)
        {
            // No retry - execute directly
            return await operation(cancellationToken);
        }

        var pipeline = CreateRetryPipeline<T>(retryOptions, operationName);
        return await pipeline.ExecuteAsync(
            async ct => await operation(ct),
            cancellationToken);
    }

    /// <summary>
    /// Executes an async operation with retry policy (no return value).
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="operationName">Name for logging purposes.</param>
    /// <param name="options">Optional retry options (uses defaults if null).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        ConnectionRetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async ct =>
            {
                await operation(ct);
                return true;
            },
            operationName,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Creates a Polly retry pipeline with the specified options.
    /// </summary>
    private ResiliencePipeline<T> CreateRetryPipeline<T>(
        ConnectionRetryOptions options,
        string operationName)
    {
        var pipelineBuilder = new ResiliencePipelineBuilder<T>();

        pipelineBuilder.AddRetry(new RetryStrategyOptions<T>
        {
            ShouldHandle = new PredicateBuilder<T>()
                .Handle<SocketException>()
                .Handle<SshConnectionException>()
                .Handle<SshOperationTimeoutException>()
                .Handle<ProxyException>()
                .Handle<TimeoutException>()
                .Handle<IOException>(ex => IsTransientNetworkError(ex)),
            MaxRetryAttempts = options.MaxRetryAttempts,
            DelayGenerator = args =>
            {
                var delay = CalculateDelay(args.AttemptNumber, options);
                return ValueTask.FromResult<TimeSpan?>(delay);
            },
            OnRetry = args =>
            {
                var delay = CalculateDelay(args.AttemptNumber, options);
                _logger.LogWarning(
                    args.Outcome.Exception,
                    "Connection attempt {Attempt}/{MaxAttempts} for '{Operation}' failed. " +
                    "Retrying in {Delay:F1}s. Error: {ErrorMessage}",
                    args.AttemptNumber + 1,
                    options.MaxRetryAttempts + 1,
                    operationName,
                    delay.TotalSeconds,
                    args.Outcome.Exception?.Message ?? "Unknown error");
                return ValueTask.CompletedTask;
            }
        });

        return pipelineBuilder.Build();
    }

    /// <summary>
    /// Calculates the delay for a retry attempt.
    /// </summary>
    private TimeSpan CalculateDelay(int attemptNumber, ConnectionRetryOptions options)
    {
        TimeSpan baseDelay;

        if (options.UseExponentialBackoff)
        {
            // Exponential backoff: delay = initialDelay * 2^attempt
            baseDelay = TimeSpan.FromMilliseconds(
                options.InitialDelay.TotalMilliseconds * Math.Pow(2, attemptNumber));
        }
        else
        {
            // Linear delay
            baseDelay = options.InitialDelay;
        }

        // Cap at max delay
        if (baseDelay > options.MaxDelay)
        {
            baseDelay = options.MaxDelay;
        }

        // Add jitter to prevent thundering herd
        if (options.JitterFactor > 0)
        {
            var jitter = baseDelay.TotalMilliseconds * options.JitterFactor * Random.Shared.NextDouble();
            baseDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds + jitter);
        }

        return baseDelay;
    }

    /// <summary>
    /// Determines if an IOException represents a transient network error.
    /// </summary>
    private static bool IsTransientNetworkError(IOException ex)
    {
        // Check if inner exception is a transient network error
        if (ex.InnerException is SocketException socketEx)
        {
            return IsTransientSocketError(socketEx.SocketErrorCode);
        }

        // Check common transient error messages
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("connection reset") ||
               message.Contains("broken pipe") ||
               message.Contains("network") ||
               message.Contains("timeout");
    }

    /// <summary>
    /// Determines if a socket error code represents a transient error.
    /// </summary>
    private static bool IsTransientSocketError(SocketError errorCode)
    {
        return errorCode switch
        {
            SocketError.ConnectionRefused => true,
            SocketError.ConnectionReset => true,
            SocketError.HostUnreachable => true,
            SocketError.NetworkUnreachable => true,
            SocketError.TimedOut => true,
            SocketError.TryAgain => true,
            SocketError.NetworkDown => true,
            SocketError.ConnectionAborted => true,
            SocketError.Interrupted => true,
            _ => false
        };
    }
}

/// <summary>
/// Interface for connection retry policy.
/// </summary>
public interface IConnectionRetryPolicy
{
    /// <summary>
    /// Executes an async operation with retry policy.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        ConnectionRetryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an async operation with retry policy (no return value).
    /// </summary>
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string operationName,
        ConnectionRetryOptions? options = null,
        CancellationToken cancellationToken = default);
}
