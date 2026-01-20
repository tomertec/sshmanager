# Terminal Services API Reference

This document provides detailed API documentation for developers who want to extend SshManager's terminal functionality or integrate with its SSH services.

**Target audience:** Developers building extensions, custom integrations, or understanding the internal architecture.

## Table of Contents

- [Overview](#overview)
- [Connection Services](#connection-services)
  - [ISshConnectionService](#isshconnectionservice)
  - [ISshConnection](#isshconnection)
  - [TerminalConnectionInfo](#terminalconnectioninfo)
- [Serial Connection Services](#serial-connection-services)
  - [ISerialConnectionService](#iserialconnectionservice)
  - [ISerialConnection](#iserialconnection)
  - [SerialConnectionInfo](#serialconnectioninfo)
- [Bridge Services](#bridge-services)
  - [SshTerminalBridge](#sshterminalbridge)
  - [SerialTerminalBridge](#serialterminalbridge)
  - [WebTerminalBridge](#webterminalbridge)
- [Terminal Control](#terminal-control)
  - [SshTerminalControl](#sshterminalcontrol)
- [SSH Agent Services](#ssh-agent-services)
  - [IAgentKeyService](#iagentkeyservice)
  - [IAgentDiagnosticsService](#iagentdiagnosticsservice)
- [Auto-Reconnect Services](#auto-reconnect-services)
  - [IAutoReconnectManager](#iautoreconnectmanager)
- [Autocompletion Services](#autocompletion-services)
  - [IAutocompletionService](#iautocompletionservice)
- [Tunnel Builder Services](#tunnel-builder-services)
  - [ITunnelBuilderService](#itunnelbuilderservice)
- [Recording Services](#recording-services)
  - [ISessionRecordingService](#isessionrecordingservice)
  - [SessionRecorder](#sessionrecorder)
- [Playback Services](#playback-services)
  - [ISessionPlaybackService](#isessionplaybackservice)
  - [PlaybackController](#playbackcontroller)
- [Export Services](#export-services)
  - [ISshConfigExportService](#isshconfigexportservice)
- [Callbacks](#callbacks)
- [Data Flow Diagrams](#data-flow-diagrams)

---

## Overview

The terminal services layer provides SSH connectivity and terminal rendering through a layered architecture:

```
┌─────────────────────────────────────────────────────────────────┐
│                    SshTerminalControl                           │
│         (WPF UserControl - Orchestrates everything)             │
└─────────────────────────────────────┬───────────────────────────┘
                                      │
          ┌───────────────────────────┼───────────────────────────┐
          │                           │                           │
          ▼                           ▼                           ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ SshTerminal     │     │ WebTerminal     │     │ WebTerminal     │
│ Bridge          │────▶│ Bridge          │────▶│ Control         │
│ (SSH ↔ C#)      │     │ (C# ↔ JS)       │     │ (xterm.js)      │
└─────────────────┘     └─────────────────┘     └─────────────────┘
          │
          ▼
┌─────────────────┐
│ SshConnection   │
│ Service         │
│ (SSH.NET)       │
└─────────────────┘
```

---

## Connection Services

### ISshConnectionService

**Namespace:** `SshManager.Terminal.Services`

The primary service for establishing SSH connections. Supports direct connections and multi-hop proxy chains.

#### Methods

##### ConnectAsync (Basic)

```csharp
Task<ISshConnection> ConnectAsync(
    TerminalConnectionInfo connectionInfo,
    uint columns = 80,
    uint rows = 24,
    CancellationToken ct = default);
```

Establishes an SSH connection without host key verification.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `connectionInfo` | `TerminalConnectionInfo` | Connection parameters (host, port, credentials) |
| `columns` | `uint` | Initial terminal width (default: 80) |
| `rows` | `uint` | Initial terminal height (default: 24) |
| `ct` | `CancellationToken` | Cancellation token |

**Returns:** `ISshConnection` - An active SSH connection with shell stream

**Throws:**
- `ArgumentNullException` - If connectionInfo is null
- `ArgumentException` - If hostname or username is empty
- `SshAuthenticationException` - If authentication fails
- `SshConnectionException` - If connection fails

**Example:**
```csharp
var connectionInfo = new TerminalConnectionInfo
{
    Hostname = "server.example.com",
    Port = 22,
    Username = "admin",
    AuthType = AuthType.SshAgent
};

var connection = await sshService.ConnectAsync(connectionInfo);
```

##### ConnectAsync (With Host Key Verification)

```csharp
Task<ISshConnection> ConnectAsync(
    TerminalConnectionInfo connectionInfo,
    HostKeyVerificationCallback? hostKeyCallback,
    uint columns = 80,
    uint rows = 24,
    CancellationToken ct = default);
```

Establishes an SSH connection with host key verification callback.

**Additional Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `hostKeyCallback` | `HostKeyVerificationCallback?` | Callback to verify server's host key |

##### ConnectAsync (Full)

```csharp
Task<ISshConnection> ConnectAsync(
    TerminalConnectionInfo connectionInfo,
    HostKeyVerificationCallback? hostKeyCallback,
    KeyboardInteractiveCallback? kbInteractiveCallback,
    uint columns = 80,
    uint rows = 24,
    CancellationToken ct = default);
```

Full connection method with host key verification and keyboard-interactive (2FA) support.

**Additional Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `kbInteractiveCallback` | `KeyboardInteractiveCallback?` | Callback for 2FA/TOTP prompts |

**Example:**
```csharp
var connection = await sshService.ConnectAsync(
    connectionInfo,
    hostKeyCallback: async (host, port, algo, fingerprint, keyBytes) =>
    {
        // Verify fingerprint against stored value
        return await VerifyHostKey(host, port, fingerprint);
    },
    kbInteractiveCallback: async (request) =>
    {
        // Prompt user for 2FA code
        request.Responses[0] = await GetTotpCode(request.Prompts[0]);
        return request;
    });
```

##### ConnectWithProxyChainAsync

```csharp
Task<ISshConnection> ConnectWithProxyChainAsync(
    IReadOnlyList<TerminalConnectionInfo> connectionChain,
    HostKeyVerificationCallback? hostKeyCallback,
    KeyboardInteractiveCallback? kbInteractiveCallback,
    uint columns = 80,
    uint rows = 24,
    CancellationToken ct = default);
```

Connects through a chain of jump hosts (ProxyJump pattern).

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `connectionChain` | `IReadOnlyList<TerminalConnectionInfo>` | Ordered list: first=jump host, last=target |

**Example:**
```csharp
// Connect: local → bastion → internal-server
var chain = new List<TerminalConnectionInfo>
{
    new() { Hostname = "bastion.example.com", Username = "admin", AuthType = AuthType.SshAgent },
    new() { Hostname = "internal.local", Username = "deploy", AuthType = AuthType.PrivateKeyFile, PrivateKeyPath = "/keys/deploy" }
};

var connection = await sshService.ConnectWithProxyChainAsync(chain, hostKeyCallback, null);
```

---

### ISshConnection

**Namespace:** `SshManager.Terminal.Services`

Represents an active SSH connection with shell stream. Implements `IDisposable` and `IAsyncDisposable`.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ShellStream` | `ShellStream` | The SSH.NET shell stream for terminal I/O |
| `IsConnected` | `bool` | Whether the connection is currently active |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `Disconnected` | `EventHandler?` | Raised when the connection is closed |

#### Methods

##### ResizeTerminal

```csharp
bool ResizeTerminal(uint columns, uint rows);
```

Sends a window change request to resize the remote terminal.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `columns` | `uint` | New terminal width |
| `rows` | `uint` | New terminal height |

**Returns:** `true` if resize succeeded, `false` otherwise

##### RunCommandAsync

```csharp
Task<string?> RunCommandAsync(string command, TimeSpan? timeout = null);
```

Executes a command on a separate channel (doesn't interfere with shell stream).

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `command` | `string` | The command to execute |
| `timeout` | `TimeSpan?` | Command timeout (default: 30 seconds) |

**Returns:** Command output as string, or `null` if execution failed

**Example:**
```csharp
// Get server stats without disrupting the terminal session
var uptime = await connection.RunCommandAsync("uptime");
var diskUsage = await connection.RunCommandAsync("df -h");
```

---

### TerminalConnectionInfo

**Namespace:** `SshManager.Terminal.Models`

Immutable record containing SSH connection parameters.

#### Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `Hostname` | `string` | Yes | - | Server hostname or IP |
| `Port` | `int` | No | 22 | SSH port |
| `Username` | `string` | Yes | - | SSH username |
| `AuthType` | `AuthType` | No | `SshAgent` | Authentication method |
| `Password` | `string?` | No | `null` | Decrypted password (for Password auth) |
| `PrivateKeyPath` | `string?` | No | `null` | Path to private key file |
| `PrivateKeyPassphrase` | `string?` | No | `null` | Passphrase for encrypted key |
| `Timeout` | `TimeSpan` | No | 30s | Connection timeout |
| `KeepAliveInterval` | `TimeSpan?` | No | `null` | Keep-alive interval |
| `HostId` | `Guid?` | No | `null` | Host entry ID for fingerprint storage |
| `SkipHostKeyVerification` | `bool` | No | `false` | Skip host key verification (insecure) |

#### Factory Method

```csharp
static TerminalConnectionInfo FromHostEntry(
    HostEntry host,
    string? decryptedPassword = null,
    TimeSpan? timeout = null,
    TimeSpan? keepAliveInterval = null);
```

Creates connection info from a stored `HostEntry` model.

---

## Serial Connection Services

### ISerialConnectionService

**Namespace:** `SshManager.Terminal.Services`

Service for establishing serial port (COM) connections. Uses a dual-driver architecture with System.IO.Ports as primary and RJCP.SerialPortStream as fallback.

#### Methods

##### ConnectAsync

```csharp
Task<ISerialConnection> ConnectAsync(
    SerialConnectionInfo connectionInfo,
    CancellationToken ct = default);
```

Establishes a serial port connection.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `connectionInfo` | `SerialConnectionInfo` | Serial port configuration |
| `ct` | `CancellationToken` | Cancellation token |

**Returns:** `ISerialConnection` - An active serial connection

**Throws:**
- `ArgumentNullException` - If connectionInfo is null
- `SerialConnectionException` - If connection fails

##### GetAvailablePortsAsync

```csharp
Task<IReadOnlyList<string>> GetAvailablePortsAsync(CancellationToken ct = default);
```

Enumerates available COM ports on the system.

**Returns:** List of port names (e.g., "COM1", "COM3")

---

### ISerialConnection

**Namespace:** `SshManager.Terminal.Services`

Represents an active serial port connection. Implements `IDisposable` and `IAsyncDisposable`.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BaseStream` | `Stream` | The underlying serial port stream for I/O |
| `IsConnected` | `bool` | Whether the connection is currently active |
| `IsOpen` | `bool` | Whether the serial port is open |
| `PortName` | `string` | The COM port name |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `Disconnected` | `EventHandler?` | Raised when the connection is closed |

#### Methods

##### SendBreak

```csharp
void SendBreak(int duration = 250);
```

Sends a break signal to the device for the specified duration in milliseconds.

##### SetDtr / SetRts

```csharp
void SetDtr(bool enabled);
void SetRts(bool enabled);
```

Controls Data Terminal Ready (DTR) and Request To Send (RTS) hardware signals.

---

### SerialConnectionInfo

**Namespace:** `SshManager.Terminal.Models`

Immutable record containing serial port connection parameters.

#### Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `PortName` | `string` | Yes | - | COM port name (e.g., "COM1") |
| `BaudRate` | `int` | No | 9600 | Communication speed |
| `DataBits` | `int` | No | 8 | Data bits per byte (5-8) |
| `StopBits` | `StopBits` | No | `One` | Stop bits configuration |
| `Parity` | `Parity` | No | `None` | Parity checking mode |
| `Handshake` | `Handshake` | No | `None` | Flow control method |
| `DtrEnable` | `bool` | No | `true` | Enable DTR signal on connect |
| `RtsEnable` | `bool` | No | `true` | Enable RTS signal on connect |
| `LocalEcho` | `bool` | No | `false` | Echo typed characters locally |
| `LineEnding` | `string` | No | `"\r\n"` | Line ending for sent commands |

#### Factory Method

```csharp
static SerialConnectionInfo FromHostEntry(HostEntry host);
```

Creates connection info from a stored `HostEntry` model with `ConnectionType.Serial`.

---

## SSH Agent Services

### IAgentKeyService

**Namespace:** `SshManager.Terminal.Services`

Service for programmatic management of SSH keys in SSH agents (Pageant and Windows OpenSSH Agent).

#### Methods

##### AddKeyToAgentAsync

```csharp
Task<bool> AddKeyToAgentAsync(
    string keyFilePath,
    string? passphrase = null,
    CancellationToken ct = default);
```

Adds a private key file to the SSH agent.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `keyFilePath` | `string` | Path to the private key file |
| `passphrase` | `string?` | Passphrase for encrypted keys |
| `ct` | `CancellationToken` | Cancellation token |

**Returns:** `true` if key was added successfully

##### AddKeyContentToAgentAsync

```csharp
Task<bool> AddKeyContentToAgentAsync(
    string keyContent,
    string? passphrase = null,
    CancellationToken ct = default);
```

Adds a private key from memory content to the SSH agent.

##### RemoveKeyFromAgentAsync

```csharp
Task<bool> RemoveKeyFromAgentAsync(
    string keyFilePath,
    CancellationToken ct = default);
```

Removes a specific key from the SSH agent. (OpenSSH Agent only)

##### RemoveAllKeysAsync

```csharp
Task<bool> RemoveAllKeysAsync(CancellationToken ct = default);
```

Removes all keys from the SSH agent. (OpenSSH Agent only)

##### GetAgentAvailabilityAsync

```csharp
Task<AgentAvailability> GetAgentAvailabilityAsync(CancellationToken ct = default);
```

Checks which SSH agents are available.

---

### IAgentDiagnosticsService

**Namespace:** `SshManager.Terminal.Services`

Service for querying SSH agent status and loaded keys.

#### Methods

##### GetAgentStatusAsync

```csharp
Task<AgentStatus> GetAgentStatusAsync(CancellationToken ct = default);
```

Gets the current status of available SSH agents.

**Returns:** `AgentStatus` containing:
- `PageantAvailable` - Whether Pageant is running
- `OpenSshAgentAvailable` - Whether Windows OpenSSH Agent is available
- `PageantKeyCount` - Number of keys loaded in Pageant
- `OpenSshAgentKeyCount` - Number of keys in OpenSSH Agent

##### GetLoadedKeysAsync

```csharp
Task<IReadOnlyList<AgentKeyInfo>> GetLoadedKeysAsync(CancellationToken ct = default);
```

Enumerates all keys currently loaded in available agents.

**Returns:** List of `AgentKeyInfo` with:
- `Fingerprint` - SHA256 fingerprint
- `Comment` - Key comment
- `Type` - Key type (RSA, Ed25519, etc.)
- `Agent` - Which agent holds the key

---

## Auto-Reconnect Services

### IAutoReconnectManager

**Namespace:** `SshManager.Terminal.Services`

Manages automatic reconnection for disconnected SSH sessions.

#### Methods

##### EnableAutoReconnect

```csharp
void EnableAutoReconnect(
    Guid sessionId,
    ConnectionRetryPolicy? policy = null);
```

Enables auto-reconnect for a session with optional custom retry policy.

##### DisableAutoReconnect

```csharp
void DisableAutoReconnect(Guid sessionId);
```

Disables auto-reconnect for a session.

##### TriggerReconnect

```csharp
Task<bool> TriggerReconnect(
    Guid sessionId,
    CancellationToken ct = default);
```

Manually triggers a reconnection attempt.

#### ConnectionRetryPolicy

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | 5 | Maximum reconnection attempts |
| `InitialDelay` | `TimeSpan` | 1s | Initial delay before first retry |
| `MaxDelay` | `TimeSpan` | 30s | Maximum delay between retries |
| `BackoffMultiplier` | `double` | 2.0 | Exponential backoff multiplier |

---

## Autocompletion Services

### IAutocompletionService

**Namespace:** `SshManager.Terminal.Services`

Provides intelligent command completion for terminal input.

#### Methods

##### GetCompletionsAsync

```csharp
Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
    string input,
    int cursorPosition,
    Guid sessionId,
    CancellationToken ct = default);
```

Gets completion suggestions for the current input.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `string` | Current command line input |
| `cursorPosition` | `int` | Cursor position in input |
| `sessionId` | `Guid` | Session ID for context |
| `ct` | `CancellationToken` | Cancellation token |

**Returns:** List of `CompletionItem` with:
- `Text` - Completion text to insert
- `DisplayText` - Text to show in popup
- `Description` - Optional description
- `Type` - Completion type (Command, File, History, etc.)

#### AutocompletionMode Enum

| Value | Description |
|-------|-------------|
| `Off` | Autocompletion disabled |
| `Manual` | Trigger with Tab key |
| `Automatic` | Show suggestions as you type |

---

## Tunnel Builder Services

### ITunnelBuilderService

**Namespace:** `SshManager.Terminal.Services`

Service for building and executing complex SSH tunnel configurations.

#### Methods

##### BuildTunnelChainAsync

```csharp
Task<TunnelExecutionPlan> BuildTunnelChainAsync(
    TunnelProfile profile,
    CancellationToken ct = default);
```

Creates an execution plan from a tunnel profile.

##### ExecuteTunnelPlanAsync

```csharp
Task<TunnelExecutionResult> ExecuteTunnelPlanAsync(
    TunnelExecutionPlan plan,
    HostKeyVerificationCallback? hostKeyCallback = null,
    CancellationToken ct = default);
```

Executes a tunnel plan, establishing all connections in order.

##### GetTunnelStatusAsync

```csharp
Task<TunnelStatus> GetTunnelStatusAsync(
    Guid profileId,
    CancellationToken ct = default);
```

Gets the current status of an active tunnel.

#### TunnelProfile Model

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Unique profile identifier |
| `Name` | `string` | Profile display name |
| `Nodes` | `ICollection<TunnelNode>` | Hosts and gateways |
| `Edges` | `ICollection<TunnelEdge>` | Connections between nodes |

#### TunnelNodeType Enum

| Value | Description |
|-------|-------------|
| `Source` | Starting point (local machine) |
| `Intermediate` | Jump host / bastion / gateway |
| `Destination` | Target server |

---

## Bridge Services

### SshTerminalBridge

**Namespace:** `SshManager.Terminal.Services`

Bridges SSH.NET `ShellStream` with terminal control. Handles bidirectional data flow.

#### Constructor

```csharp
SshTerminalBridge(ShellStream shellStream, ILogger<SshTerminalBridge>? logger = null)
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TotalBytesSent` | `long` | Cumulative bytes sent to server |
| `TotalBytesReceived` | `long` | Cumulative bytes received from server |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `DataReceived` | `Action<byte[]>?` | Raw bytes received from SSH server |
| `Disconnected` | `EventHandler?` | Connection was closed |

#### Methods

##### StartReading

```csharp
void StartReading();
```

Starts the background read loop. Data is delivered via `DataReceived` event.

##### SendData

```csharp
void SendData(byte[] data);
void SendData(ReadOnlySpan<char> text);
```

Sends raw bytes or character data to the SSH server.

##### SendText

```csharp
void SendText(string text);
```

Sends a string as UTF-8 encoded bytes.

##### SendCommand

```csharp
void SendCommand(string command);
```

Sends a command string followed by carriage return (`\r`).

**Example:**
```csharp
var bridge = new SshTerminalBridge(connection.ShellStream, logger);
bridge.DataReceived += data => terminalControl.WriteData(Encoding.UTF8.GetString(data));
bridge.Disconnected += (s, e) => ShowDisconnectedMessage();
bridge.StartReading();

// Send user input
bridge.SendText("ls -la\r");
```

---

### WebTerminalBridge

**Namespace:** `SshManager.Terminal.Services`

Bridges C# with WebView2/xterm.js terminal. Handles JSON message passing.

#### Constructor

```csharp
WebTerminalBridge(ILogger<WebTerminalBridge>? logger = null)
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `WebView` | `WebView2?` | The associated WebView2 control |
| `IsReady` | `bool` | Whether terminal is initialized |
| `Columns` | `int` | Current terminal width |
| `Rows` | `int` | Current terminal height |
| `FontSize` | `double` | Current font size |

#### Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `DefaultFontSize` | 14 | Default font size |
| `MinFontSize` | 8 | Minimum zoom level |
| `MaxFontSize` | 32 | Maximum zoom level |
| `FontSizeStep` | 1 | Zoom increment |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `InputReceived` | `Action<string>?` | User typed in terminal |
| `TerminalReady` | `Action?` | Terminal initialized |
| `TerminalResized` | `Action<int, int>?` | Terminal resized (cols, rows) |

#### Methods

##### InitializeAsync

```csharp
Task InitializeAsync(WebView2 webView);
```

Connects bridge to a WebView2 control and sets up message handling.

##### WriteData

```csharp
void WriteData(string data);
```

Sends text to terminal for display. Buffers if terminal not ready. Uses batching for performance.

##### Resize

```csharp
void Resize(int cols, int rows);
```

Resizes the terminal grid.

##### SetTheme

```csharp
void SetTheme(object theme);
```

Sets terminal colors. Theme object should match xterm.js ITheme interface.

##### SetFont

```csharp
void SetFont(string? fontFamily, double fontSize);
```

Sets terminal font options.

##### Focus / Clear / Fit

```csharp
void Focus();   // Focus terminal for input
void Clear();   // Clear terminal display
void Fit();     // Fit terminal to container size
```

##### Zoom Methods

```csharp
bool ZoomIn();      // Increase font size, returns false if at max
bool ZoomOut();     // Decrease font size, returns false if at min
void ResetZoom();   // Reset to default font size
```

#### Message Protocol

The bridge communicates with xterm.js using JSON messages:

**C# → JavaScript:**
```json
{ "type": "write", "data": "Hello, World!" }
{ "type": "resize", "cols": 120, "rows": 40 }
{ "type": "setTheme", "theme": { "background": "#1e1e1e", "foreground": "#d4d4d4" } }
{ "type": "setFont", "fontFamily": "Cascadia Mono", "fontSize": 14 }
{ "type": "focus" }
{ "type": "clear" }
{ "type": "fit" }
```

**JavaScript → C#:**
```json
{ "type": "input", "data": "user typed text" }
{ "type": "ready" }
{ "type": "resized", "cols": 120, "rows": 40 }
```

---

## Terminal Control

### SshTerminalControl

**Namespace:** `SshManager.Terminal.Controls`

WPF UserControl that orchestrates SSH sessions with xterm.js rendering.

#### Constructor

```csharp
SshTerminalControl();
SshTerminalControl(
    ITerminalKeyboardHandler? keyboardHandler,
    ITerminalClipboardService? clipboardService,
    ITerminalConnectionHandler? connectionHandler);
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TerminalFontFamily` | `string` | Font family (default: "Cascadia Mono") |
| `TerminalFontSize` | `double` | Font size (default: 14) |
| `IsConnected` | `bool` | Connection state |
| `ScrollbackBufferSize` | `int` | Max lines in scrollback buffer |
| `MaxLinesInMemory` | `int` | Lines before spilling to disk |
| `IsPrimaryPane` | `bool` | Whether this is the primary pane |
| `CurrentTheme` | `TerminalTheme?` | Currently applied theme |
| `BroadcastService` | `IBroadcastInputService?` | Multi-terminal broadcast |
| `OutputBuffer` | `TerminalOutputBuffer` | Buffer for search/export |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `TitleChanged` | `EventHandler<string>?` | Terminal title changed |
| `Disconnected` | `EventHandler?` | SSH connection closed |

#### Connection Methods

##### ConnectAsync

```csharp
Task ConnectAsync(
    ISshConnectionService sshService,
    TerminalConnectionInfo connectionInfo,
    HostKeyVerificationCallback? hostKeyCallback = null,
    CancellationToken cancellationToken = default);

Task ConnectAsync(
    ISshConnectionService sshService,
    TerminalConnectionInfo connectionInfo,
    HostKeyVerificationCallback? hostKeyCallback,
    KeyboardInteractiveCallback? kbInteractiveCallback,
    CancellationToken cancellationToken = default);
```

Connects to an SSH server.

##### ConnectWithProxyChainAsync

```csharp
Task ConnectWithProxyChainAsync(
    ISshConnectionService sshService,
    IReadOnlyList<TerminalConnectionInfo> connectionChain,
    HostKeyVerificationCallback? hostKeyCallback,
    KeyboardInteractiveCallback? kbInteractiveCallback,
    CancellationToken cancellationToken = default);
```

Connects through jump hosts.

##### AttachToSessionAsync

```csharp
Task AttachToSessionAsync(TerminalSession session);
```

Attaches to an existing connected session (for split panes/mirroring).

##### Disconnect

```csharp
void Disconnect();
```

Disconnects and cleans up resources.

#### UI Methods

```csharp
void FocusInput();                      // Focus the terminal
void SendCommand(string command);       // Send command + Enter
void ApplyTheme(TerminalTheme theme);   // Apply color theme
void ShowFindOverlay();                 // Show search overlay
void HideFindOverlay();                 // Hide search overlay
void CopyToClipboard();                 // Copy selected text
void PasteFromClipboard();              // Paste from clipboard
string GetOutputText();                 // Get all terminal output
void ClearOutputBuffer();               // Clear output buffer
```

#### Service Injection

```csharp
void SetBroadcastService(IBroadcastInputService? service);
void SetServerStatsService(IServerStatsService? service);
```

---

## Recording Services

### ISessionRecordingService

**Namespace:** `SshManager.Terminal.Services.Recording`

Service for recording terminal sessions in ASCIINEMA v2 format.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `RecordingsDirectory` | `string` | Directory where recordings are stored |

#### Methods

##### StartRecordingAsync

```csharp
Task<SessionRecorder> StartRecordingAsync(
    Guid sessionId,
    HostEntry? host,
    int cols,
    int rows,
    string? title = null,
    CancellationToken ct = default);
```

Starts recording a terminal session.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `sessionId` | `Guid` | Unique session identifier |
| `host` | `HostEntry?` | Host being connected to (optional) |
| `cols` | `int` | Terminal width in columns |
| `rows` | `int` | Terminal height in rows |
| `title` | `string?` | Recording title (defaults to host info) |
| `ct` | `CancellationToken` | Cancellation token |

**Returns:** `SessionRecorder` - The active session recorder

##### StopRecordingAsync

```csharp
Task StopRecordingAsync(Guid sessionId, CancellationToken ct = default);
```

Stops recording a session and finalizes the recording file.

##### GetRecorder

```csharp
SessionRecorder? GetRecorder(Guid sessionId);
```

Gets the active recorder for a session, or `null` if not recording.

##### IsRecording

```csharp
bool IsRecording(Guid sessionId);
```

Checks if a session is currently being recorded.

##### LoadRecordingAsync

```csharp
Task<List<RecordingFrame>> LoadRecordingAsync(Guid recordingId, CancellationToken ct = default);
```

Loads a recording from disk for playback.

---

### SessionRecorder

**Namespace:** `SshManager.Terminal.Services.Recording`

Active recorder for a terminal session.

#### Methods

##### RecordOutput

```csharp
void RecordOutput(string data);
```

Records terminal output data with timestamp.

##### RecordInput

```csharp
void RecordInput(string data);
```

Records user input data with timestamp.

**Example:**
```csharp
var recordingService = services.GetRequiredService<ISessionRecordingService>();

// Start recording
var recorder = await recordingService.StartRecordingAsync(
    sessionId: Guid.NewGuid(),
    host: hostEntry,
    cols: 120,
    rows: 40,
    title: "Database maintenance session");

// During session, capture output
sshBridge.DataReceived += data =>
{
    recorder.RecordOutput(Encoding.UTF8.GetString(data));
};

// Stop recording when done
await recordingService.StopRecordingAsync(sessionId);
```

---

## Playback Services

### ISessionPlaybackService

**Namespace:** `SshManager.Terminal.Services.Playback`

Service for playing back recorded terminal sessions.

#### Methods

##### CreatePlaybackAsync

```csharp
Task<PlaybackController> CreatePlaybackAsync(string filePath, CancellationToken ct = default);
```

Creates a playback controller for the specified recording file.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `filePath` | `string` | Path to the .cast recording file |
| `ct` | `CancellationToken` | Cancellation token |

**Returns:** `PlaybackController` - Configured playback controller

**Throws:**
- `ArgumentNullException` - If filePath is null
- `FileNotFoundException` - If file doesn't exist
- `InvalidDataException` - If file format is invalid

##### LoadRecordingAsync

```csharp
Task<AsciinemaReader> LoadRecordingAsync(string filePath, CancellationToken ct = default);
```

Loads a recording without creating a playback controller (for metadata inspection).

---

### PlaybackController

**Namespace:** `SshManager.Terminal.Services.Playback`

Controls playback of a recorded session.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Duration` | `TimeSpan` | Total duration of the recording |
| `Position` | `TimeSpan` | Current playback position |
| `PlaybackSpeed` | `double` | Playback speed multiplier (0.5 to 4.0) |
| `IsPlaying` | `bool` | Whether playback is active |
| `IsPaused` | `bool` | Whether playback is paused |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `OutputReceived` | `Action<string>?` | Fired when output data is ready to display |
| `PositionChanged` | `Action<TimeSpan>?` | Fired when playback position changes |
| `PlaybackCompleted` | `EventHandler?` | Fired when playback reaches the end |

#### Methods

```csharp
void Play();           // Start or resume playback
void Pause();          // Pause playback
void Stop();           // Stop and reset to beginning
void Seek(TimeSpan position);  // Seek to position
void SetSpeed(double speed);   // Set playback speed (0.5 to 4.0)
```

**Example:**
```csharp
var playbackService = services.GetRequiredService<ISessionPlaybackService>();
var controller = await playbackService.CreatePlaybackAsync(
    @"%LocalAppData%\SshManager\recordings\session123.cast");

// Wire up events
controller.OutputReceived += data => webTerminalBridge.WriteData(data);
controller.PositionChanged += pos => UpdateProgressBar(pos, controller.Duration);
controller.PlaybackCompleted += (s, e) => ShowCompletedMessage();

// Control playback
controller.SetSpeed(2.0);  // 2x speed
controller.Play();

// Later...
controller.Seek(TimeSpan.FromMinutes(5));  // Jump to 5 minute mark
controller.Pause();
```

---

## Export Services

### ISshConfigExportService

**Namespace:** `SshManager.Terminal.Services`

Service for exporting hosts to OpenSSH config format.

#### Methods

##### GenerateConfig

```csharp
string GenerateConfig(IEnumerable<HostEntry> hosts, SshConfigExportOptions options);
```

Generates SSH config content from hosts.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `hosts` | `IEnumerable<HostEntry>` | Hosts to export |
| `options` | `SshConfigExportOptions` | Export configuration |

**Returns:** SSH config file content as string

##### ExportToFileAsync

```csharp
Task ExportToFileAsync(
    string filePath,
    IEnumerable<HostEntry> hosts,
    SshConfigExportOptions options,
    CancellationToken ct = default);
```

Exports SSH config directly to a file.

#### SshConfigExportOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeComments` | `bool` | `true` | Include metadata comments |
| `IncludeGroups` | `bool` | `true` | Include group name headers |
| `IncludePortForwarding` | `bool` | `true` | Include port forwarding rules |
| `UseProxyJump` | `bool` | `true` | Use ProxyJump (modern) vs ProxyCommand (legacy) |

**Example:**
```csharp
var exportService = services.GetRequiredService<ISshConfigExportService>();

var options = new SshConfigExportOptions
{
    IncludeComments = true,
    IncludeGroups = true,
    IncludePortForwarding = true,
    UseProxyJump = true  // Use modern ProxyJump directive
};

// Generate config string
var config = exportService.GenerateConfig(hosts, options);

// Or export directly to file
await exportService.ExportToFileAsync(
    @"C:\Users\me\.ssh\config",
    hosts,
    options);
```

**Generated Output Example:**
```
# SshManager Export - 2024-01-15

# Group: Production Servers
Host prod-web-1
    HostName 10.0.1.10
    User deploy
    Port 22
    IdentityFile ~/.ssh/deploy_key

Host prod-db-1
    HostName 10.0.1.20
    User dbadmin
    Port 22
    ProxyJump prod-web-1

# Group: Development
Host dev-server
    HostName dev.example.com
    User developer
    LocalForward 8080 localhost:80
```

---

## Callbacks

### HostKeyVerificationCallback

```csharp
delegate Task<bool> HostKeyVerificationCallback(
    string hostname,
    int port,
    string algorithm,
    string fingerprint,
    byte[] keyBytes);
```

Called during SSH handshake to verify the server's host key.

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `hostname` | `string` | Server hostname |
| `port` | `int` | Server port |
| `algorithm` | `string` | Key algorithm (e.g., "ssh-ed25519", "ssh-rsa") |
| `fingerprint` | `string` | SHA256 fingerprint (base64, no padding) |
| `keyBytes` | `byte[]` | Raw public key bytes |

**Returns:** `true` to accept, `false` to reject connection

### KeyboardInteractiveCallback

```csharp
delegate Task<AuthenticationRequest?> KeyboardInteractiveCallback(
    AuthenticationRequest request);
```

Called when server requires keyboard-interactive authentication (2FA, TOTP).

**Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| `request` | `AuthenticationRequest` | Contains prompts and responses array |

**Returns:** Request with responses filled in, or `null` to cancel

---

## Data Flow Diagrams

### Connection Establishment

```
┌──────────────────────────────────────────────────────────────────────────┐
│                         CONNECTION ESTABLISHMENT                          │
└──────────────────────────────────────────────────────────────────────────┘

User clicks "Connect"
        │
        ▼
┌───────────────────┐
│ MainWindowViewModel│
│ .ConnectToHostAsync│
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐     Creates tab in UI
│ TerminalSession   │◄────────────────────────
│ Manager           │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐     Password auth?
│ SecretProtector   │◄──── Decrypt password
│ .Unprotect()      │      using DPAPI
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐     Creates ConnectionInfo
│ SshConnection     │     with auth methods
│ Service           │
└─────────┬─────────┘
          │
          ├──── Host Key Callback ────▶ HostKeyVerificationDialog
          │                             (if new/changed key)
          │
          ├──── KB-Interactive ───────▶ KeyboardInteractiveDialog
          │                             (if 2FA required)
          │
          ▼
┌───────────────────┐
│ SSH.NET           │     TCP connect + auth
│ SshClient         │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ SshClient         │     Opens PTY session
│ .CreateShellStream│
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐     Wraps for data flow
│ SshTerminalBridge │
└─────────┬─────────┘
          │
          ▼
        READY
```

### Terminal Data Flow

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           TERMINAL DATA FLOW                              │
└──────────────────────────────────────────────────────────────────────────┘

              SSH Server Output                    User Keyboard Input
                    │                                      │
                    ▼                                      ▼
        ┌───────────────────┐                  ┌───────────────────┐
        │   SSH.NET         │                  │   xterm.js        │
        │   ShellStream     │                  │   (WebView2)      │
        └─────────┬─────────┘                  └─────────┬─────────┘
                  │ byte[]                               │ JSON
                  ▼                                      ▼
        ┌───────────────────┐                  ┌───────────────────┐
        │ SshTerminalBridge │                  │ WebTerminalBridge │
        │ .DataReceived     │                  │ .InputReceived    │
        └─────────┬─────────┘                  └─────────┬─────────┘
                  │ byte[]                               │ string
                  ▼                                      │
        ┌───────────────────┐                           │
        │ SshTerminalControl│                           │
        │ .OnSshDataReceived│                           │
        └─────────┬─────────┘                           │
                  │ UTF-8 decode                        │
                  ▼                                      │
        ┌───────────────────┐                           │
        │ TerminalOutput    │     Search buffer         │
        │ Buffer.Append     │◄───────────────           │
        └─────────┬─────────┘                           │
                  │ string                              │
                  ▼                                      │
        ┌───────────────────┐                           │
        │ WebTerminalBridge │     Batched for           │
        │ .WriteData        │     performance           │
        └─────────┬─────────┘                           │
                  │ JSON                                │
                  ▼                                      │
        ┌───────────────────┐                           │
        │   xterm.js        │     Renders ANSI          │
        │   terminal.write()│     escape sequences      │
        └───────────────────┘                           │
                                                        │
                  ┌─────────────────────────────────────┘
                  │
                  ▼
        ┌───────────────────┐
        │ SshTerminalControl│     Broadcast check
        │ .OnTerminalInput  │
        └─────────┬─────────┘
                  │
                  ▼
        ┌───────────────────┐
        │ SshTerminalBridge │     UTF-8 encode
        │ .SendText         │
        └─────────┬─────────┘
                  │ byte[]
                  ▼
        ┌───────────────────┐
        │   SSH.NET         │
        │   ShellStream     │
        └─────────┬─────────┘
                  │
                  ▼
              SSH Server
```

### Authentication Flow

```
┌──────────────────────────────────────────────────────────────────────────┐
│                        AUTHENTICATION DECISION TREE                       │
└──────────────────────────────────────────────────────────────────────────┘

                    ┌─────────────────┐
                    │   AuthType?     │
                    └────────┬────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
         ▼                   ▼                   ▼
    ┌─────────┐        ┌─────────┐        ┌─────────┐
    │SshAgent │        │PrivKey  │        │Password │
    └────┬────┘        └────┬────┘        └────┬────┘
         │                  │                  │
         ▼                  ▼                  ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ Load keys from  │  │ Read key file   │  │ DPAPI decrypt   │
│ ~/.ssh/ folder  │  │ from path       │  │ stored password │
│ or SSH Agent    │  │                 │  │                 │
└────────┬────────┘  └────────┬────────┘  └────────┬────────┘
         │                    │                    │
         │              ┌─────┴─────┐              │
         │              │Encrypted? │              │
         │              └─────┬─────┘              │
         │           Yes      │      No            │
         │            ┌───────┴───────┐            │
         │            ▼               │            │
         │   ┌─────────────────┐      │            │
         │   │ Prompt for      │      │            │
         │   │ passphrase      │      │            │
         │   └────────┬────────┘      │            │
         │            │               │            │
         │            ▼               │            │
         │   ┌─────────────────┐      │            │
         │   │ CredentialCache │      │            │
         │   │ (optional)      │      │            │
         │   └────────┬────────┘      │            │
         │            │               │            │
         └────────────┴───────────────┴────────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │ SSH.NET auth    │
                    │ methods array   │
                    └────────┬────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │ Connect with    │
                    │ SshClient       │
                    └────────┬────────┘
                             │
               ┌─────────────┴─────────────┐
               │                           │
               ▼                           ▼
    ┌─────────────────┐         ┌─────────────────┐
    │ 2FA Required?   │         │ Auth Success    │
    └────────┬────────┘         └─────────────────┘
             │ Yes
             ▼
    ┌─────────────────┐
    │ Keyboard-       │
    │ Interactive     │
    │ Dialog          │
    └────────┬────────┘
             │
             ▼
    ┌─────────────────┐
    │ Submit TOTP     │
    │ Response        │
    └─────────────────┘
```

### ProxyJump Connection Chain

```
┌──────────────────────────────────────────────────────────────────────────┐
│                      PROXY CHAIN CONNECTION FLOW                          │
└──────────────────────────────────────────────────────────────────────────┘

Example chain: Local → Bastion → Jump → Target

Step 1: Connect to Bastion (first hop)
┌────────┐    SSH (port 22)    ┌─────────┐
│ Local  │ ─────────────────▶  │ Bastion │
│ Machine│                     │         │
└────────┘                     └────┬────┘
                                   │
                                   │ Forward port 10022 → Jump:22
                                   ▼
Step 2: Connect to Jump through forwarded port
┌────────┐    localhost:10022   ┌─────────┐    SSH (port 22)    ┌──────┐
│ Local  │ ──────────────────▶  │ Bastion │ ─────────────────▶  │ Jump │
│ Machine│                      │ (tunnel)│                     │      │
└────────┘                      └─────────┘                     └──┬───┘
                                                                   │
                                                                   │ Forward port 10023 → Target:22
                                                                   ▼
Step 3: Connect to Target through chained tunnels
┌────────┐   localhost:10023    ┌─────────┐      ┌──────┐      ┌────────┐
│ Local  │ ──────────────────▶  │ Bastion │ ───▶ │ Jump │ ───▶ │ Target │
│ Machine│                      │         │      │(tunnel)     │        │
└────────┘                      └─────────┘      └──────┘      └────────┘

Final state:
┌────────────────────────────────────────────────────────────────────────┐
│                                                                        │
│  Local:10022  ──tunnel──▶  Bastion ──▶  Jump:22                       │
│  Local:10023  ──tunnel──▶  Bastion ──▶  Jump ──▶  Target:22           │
│                                                                        │
│  ProxyChainSshConnection wraps:                                        │
│  - Target SshClient (active shell stream)                             │
│  - Jump SshClient (intermediate)                                       │
│  - Bastion SshClient (first hop)                                       │
│  - ForwardedPort objects for cleanup                                   │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Error Handling

### Common Exceptions

| Exception | Cause | Resolution |
|-----------|-------|------------|
| `SshAuthenticationException` | Invalid credentials | Check username, password, or key |
| `SshConnectionException` | Network/server error | Verify server is reachable |
| `InvalidOperationException` | Host key rejected | User declined host key verification |
| `SocketException` | Network unreachable | Check network connectivity |
| `TimeoutException` | Connection timeout | Increase timeout or check firewall |

### Best Practices

1. **Always handle `Disconnected` events** to update UI state
2. **Dispose connections properly** using `await using` or explicit `Dispose()`
3. **Use cancellation tokens** for long-running operations
4. **Validate connection info** before attempting connection
5. **Implement host key verification** to prevent MITM attacks

---

## Thread Safety

| Component | Thread Safety | Notes |
|-----------|---------------|-------|
| `SshConnectionService` | Thread-safe | All methods are async |
| `SshTerminalBridge` | Partial | Events may fire on background threads |
| `WebTerminalBridge` | Partial | UI dispatch handled internally |
| `SshTerminalControl` | UI thread only | All methods must be called on UI thread |

---

## See Also

- [Architecture Overview](./ARCHITECTURE.md) - System design and patterns
- [Getting Started](./GETTING_STARTED.md) - User guide
- [CLAUDE.md](../CLAUDE.md) - Developer reference
