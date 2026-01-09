using Microsoft.Extensions.Logging;

namespace SshManager.Core.Logging;

/// <summary>
/// Provides structured logging scope helpers for consistent correlation across the application.
/// Using scopes adds contextual properties to all log entries within the scope, making it
/// easier to trace operations through logs.
/// </summary>
public static class LoggingScopes
{
    /// <summary>
    /// Creates a logging scope for a terminal session.
    /// Adds SessionId to all log entries within the scope.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    /// <example>
    /// <code>
    /// using (LoggingScopes.ForSession(_logger, session.Id))
    /// {
    ///     _logger.LogInformation("Starting connection");
    ///     // All logs here include SessionId
    /// }
    /// </code>
    /// </example>
    public static IDisposable? ForSession(ILogger logger, Guid sessionId)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = sessionId
        });
    }

    /// <summary>
    /// Creates a logging scope for a host entry.
    /// Adds HostId and HostName to all log entries within the scope.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="hostId">The host identifier.</param>
    /// <param name="hostName">The host display name or hostname.</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    public static IDisposable? ForHost(ILogger logger, Guid hostId, string? hostName = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["HostId"] = hostId
        };
        
        if (!string.IsNullOrEmpty(hostName))
        {
            scope["HostName"] = hostName;
        }
        
        return logger.BeginScope(scope);
    }

    /// <summary>
    /// Creates a logging scope for a connection operation.
    /// Adds SessionId, HostId, Hostname, and Port to all log entries within the scope.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="hostId">The host identifier (optional).</param>
    /// <param name="hostname">The connection hostname.</param>
    /// <param name="port">The connection port.</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    public static IDisposable? ForConnection(
        ILogger logger,
        Guid sessionId,
        Guid? hostId,
        string hostname,
        int port)
    {
        var scope = new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["Hostname"] = hostname,
            ["Port"] = port
        };
        
        if (hostId.HasValue)
        {
            scope["HostId"] = hostId.Value;
        }
        
        return logger.BeginScope(scope);
    }

    /// <summary>
    /// Creates a logging scope for an SFTP operation.
    /// Adds SessionId, Hostname, and Operation to all log entries within the scope.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="hostname">The connection hostname.</param>
    /// <param name="operation">The SFTP operation name (e.g., "Upload", "Download").</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    public static IDisposable? ForSftpOperation(
        ILogger logger,
        Guid sessionId,
        string hostname,
        string operation)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["Hostname"] = hostname,
            ["SftpOperation"] = operation
        });
    }

    /// <summary>
    /// Creates a logging scope for a port forwarding operation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="forwardingType">The type of forwarding (Local, Remote, Dynamic).</param>
    /// <param name="localPort">The local port number.</param>
    /// <param name="remoteHost">The remote host (for local forwarding).</param>
    /// <param name="remotePort">The remote port (for local forwarding).</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    public static IDisposable? ForPortForwarding(
        ILogger logger,
        Guid sessionId,
        string forwardingType,
        int localPort,
        string? remoteHost = null,
        int? remotePort = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["ForwardingType"] = forwardingType,
            ["LocalPort"] = localPort
        };
        
        if (!string.IsNullOrEmpty(remoteHost))
        {
            scope["RemoteHost"] = remoteHost;
        }
        
        if (remotePort.HasValue)
        {
            scope["RemotePort"] = remotePort.Value;
        }
        
        return logger.BeginScope(scope);
    }

    /// <summary>
    /// Creates a logging scope for a background service operation.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceName">The name of the background service.</param>
    /// <param name="operationId">An optional operation identifier for correlation.</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    public static IDisposable? ForBackgroundService(
        ILogger logger,
        string serviceName,
        string? operationId = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["ServiceName"] = serviceName
        };
        
        if (!string.IsNullOrEmpty(operationId))
        {
            scope["OperationId"] = operationId;
        }
        
        return logger.BeginScope(scope);
    }

    /// <summary>
    /// Creates a logging scope with custom properties.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="properties">The properties to add to the scope.</param>
    /// <returns>A disposable scope that should be used with 'using'.</returns>
    public static IDisposable? WithProperties(
        ILogger logger,
        params (string Key, object Value)[] properties)
    {
        var scope = new Dictionary<string, object>();
        foreach (var (key, value) in properties)
        {
            scope[key] = value;
        }
        return logger.BeginScope(scope);
    }
}
