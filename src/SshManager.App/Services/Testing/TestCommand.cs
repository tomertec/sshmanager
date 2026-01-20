#if DEBUG
using System.Text.Json.Serialization;

namespace SshManager.App.Services.Testing;

/// <summary>
/// Represents a command sent to the test server for UI automation.
/// </summary>
public class TestCommand
{
    /// <summary>
    /// The type of command to execute.
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Optional target element identifier (AutomationId, Name, or ClassName).
    /// </summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>
    /// Optional value for commands that need additional data (e.g., text to type).
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Optional parameters as key-value pairs for complex commands.
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }

    /// <summary>
    /// Timeout in milliseconds for commands that may take time.
    /// </summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30000;
}

/// <summary>
/// Supported test command actions.
/// </summary>
public static class TestActions
{
    // Connection & Health
    public const string Ping = "ping";
    public const string GetState = "get-state";

    // Screenshots
    public const string Screenshot = "screenshot";
    public const string ScreenshotElement = "screenshot-element";

    // UI Element Discovery
    public const string ListElements = "list-elements";
    public const string GetElement = "get-element";
    public const string FindElement = "find-element";
    public const string GetVisualTree = "get-visual-tree";

    // UI Interaction
    public const string Click = "click";
    public const string DoubleClick = "double-click";
    public const string RightClick = "right-click";
    public const string Type = "type";
    public const string Clear = "clear";
    public const string Focus = "focus";
    public const string SendKeys = "send-keys";

    // Property Access
    public const string GetProperty = "get-property";
    public const string SetProperty = "set-property";
    public const string GetText = "get-text";

    // Commands & Actions
    public const string InvokeCommand = "invoke-command";
    public const string InvokeButton = "invoke-button";

    // Navigation & Dialogs
    public const string OpenDialog = "open-dialog";
    public const string CloseDialog = "close-dialog";
    public const string GetDialogs = "get-dialogs";
    public const string SelectTab = "select-tab";

    // Host Management
    public const string GetHosts = "get-hosts";
    public const string SelectHost = "select-host";
    public const string ConnectHost = "connect-host";
    public const string DisconnectSession = "disconnect-session";

    // Session Management
    public const string GetSessions = "get-sessions";
    public const string SelectSession = "select-session";
    public const string SendToTerminal = "send-to-terminal";
    public const string GetTerminalOutput = "get-terminal-output";

    // Settings
    public const string GetSettings = "get-settings";
    public const string SetSetting = "set-setting";

    // Wait & Sync
    public const string Wait = "wait";
    public const string WaitForElement = "wait-for-element";
    public const string WaitForDialog = "wait-for-dialog";
}
#endif
