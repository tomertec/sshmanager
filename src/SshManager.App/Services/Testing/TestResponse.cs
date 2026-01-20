#if DEBUG
using System.Text.Json.Serialization;

namespace SshManager.App.Services.Testing;

/// <summary>
/// Response from the test server after executing a command.
/// </summary>
public class TestResponse
{
    /// <summary>
    /// Whether the command executed successfully.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the command failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Result data from the command (type depends on the command).
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    /// <summary>
    /// Base64-encoded screenshot image (PNG format).
    /// </summary>
    [JsonPropertyName("screenshot")]
    public string? Screenshot { get; set; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Timestamp of the response.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public static TestResponse Ok(object? data = null) => new()
    {
        Success = true,
        Data = data
    };

    public static TestResponse WithScreenshot(string base64Screenshot, object? data = null) => new()
    {
        Success = true,
        Screenshot = base64Screenshot,
        Data = data
    };

    public static TestResponse Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// Information about a UI element.
/// </summary>
public class ElementInfo
{
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("className")]
    public string? ClassName { get; set; }

    [JsonPropertyName("controlType")]
    public string? ControlType { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("bounds")]
    public BoundsInfo? Bounds { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("children")]
    public List<ElementInfo>? Children { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>
/// Bounding rectangle information.
/// </summary>
public class BoundsInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

/// <summary>
/// Application state information.
/// </summary>
public class AppStateInfo
{
    [JsonPropertyName("isReady")]
    public bool IsReady { get; set; }

    [JsonPropertyName("mainWindowTitle")]
    public string? MainWindowTitle { get; set; }

    [JsonPropertyName("hostCount")]
    public int HostCount { get; set; }

    [JsonPropertyName("sessionCount")]
    public int SessionCount { get; set; }

    [JsonPropertyName("activeSessionId")]
    public Guid? ActiveSessionId { get; set; }

    [JsonPropertyName("selectedHostId")]
    public Guid? SelectedHostId { get; set; }

    [JsonPropertyName("isConnecting")]
    public bool IsConnecting { get; set; }

    [JsonPropertyName("openDialogs")]
    public List<string>? OpenDialogs { get; set; }
}

/// <summary>
/// Host entry information for test purposes.
/// </summary>
public class HostInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("groupId")]
    public Guid? GroupId { get; set; }

    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }
}

/// <summary>
/// Terminal session information for test purposes.
/// </summary>
public class SessionInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("hostId")]
    public Guid? HostId { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
#endif
