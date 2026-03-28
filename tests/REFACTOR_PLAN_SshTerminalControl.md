# Refactoring Plan: SshTerminalControl.xaml.cs

**Current Size:** 1,785 lines
**Target Size:** ~400-500 lines
**Priority:** High (most impactful refactor in codebase)

## Executive Summary

The `SshTerminalControl.xaml.cs` is the main terminal control that orchestrates SSH and Serial sessions. While some services have already been extracted (keyboard handling, clipboard, connection handling, autocompletion, serial signals, auto-reconnect), the file still contains multiple responsibilities that can be further modularized.

## Current Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        SshTerminalControl (1,785 lines)                  │
├─────────────────────────────────────────────────────────────────────────┤
│  ALREADY EXTRACTED (via DI):                                            │
│  ├── ITerminalKeyboardHandler      (~50 lines saved)                    │
│  ├── ITerminalClipboardService     (~30 lines saved)                    │
│  ├── ITerminalConnectionHandler    (~100 lines saved)                   │
│  ├── ITerminalAutocompletionHandler (~80 lines saved)                   │
│  ├── ISerialSignalController       (~100 lines saved)                   │
│  └── IAutoReconnectManager         (~60 lines saved)                    │
├─────────────────────────────────────────────────────────────────────────┤
│  REMAINING RESPONSIBILITIES TO EXTRACT:                                 │
│  ├── Find/Search Overlay Management        (~40 lines)                  │
│  ├── Status Overlay Management             (~30 lines)                  │
│  ├── Stats Collection Orchestration        (~80 lines)                  │
│  ├── Theme & Font Management               (~50 lines)                  │
│  ├── Output Processing (UTF-8/Buffer)      (~60 lines)                  │
│  ├── SSH Connection Lifecycle              (~180 lines)                 │
│  ├── Serial Connection Lifecycle           (~200 lines)                 │
│  ├── Session Attachment/Lifecycle          (~100 lines)                 │
│  └── Service Event Forwarding              (~80 lines)                  │
└─────────────────────────────────────────────────────────────────────────┘
```

## Proposed Services to Extract

### 1. ITerminalSearchCoordinator
**Lines to extract:** ~40 lines
**Location:** `src/SshManager.Terminal/Services/TerminalSearchCoordinator.cs`

**Responsibilities:**
- Coordinate find overlay show/hide operations
- Manage search service initialization
- Handle navigation to search results
- Track search result state changes

**Interface:**
```csharp
public interface ITerminalSearchCoordinator
{
    bool IsSearchVisible { get; }

    void Initialize(TerminalOutputBuffer outputBuffer);
    void ShowSearch();
    void HideSearch();
    void NavigateToResult(int lineIndex);

    event EventHandler? SearchClosed;
    event EventHandler? ResultsChanged;
}
```

**Methods to extract from SshTerminalControl:**
- `ShowFindOverlay()` (lines 509-512)
- `HideFindOverlay()` (lines 517-521)
- `FindOverlay_CloseRequested()` (lines 523-526)
- `FindOverlay_NavigateToLine()` (lines 528-533)
- `FindOverlay_SearchResultsChanged()` (lines 535-538)

---

### 2. ITerminalStatusDisplay
**Lines to extract:** ~30 lines
**Location:** `src/SshManager.Terminal/Services/TerminalStatusDisplay.cs`

**Responsibilities:**
- Display connection status messages
- Show/hide progress indicator
- Manage status overlay visibility

**Interface:**
```csharp
public interface ITerminalStatusDisplay
{
    bool IsVisible { get; }
    string CurrentMessage { get; }

    void ShowConnecting(string message = "Connecting...");
    void ShowDisconnected(string message = "Disconnected");
    void ShowError(string message);
    void ShowReconnecting(int attempt, int maxAttempts);
    void Hide();
}
```

**Methods to extract from SshTerminalControl:**
- `ShowStatus(string message)` (lines 1250-1257)
- `HideStatus()` (lines 1259-1262)

---

### 3. ITerminalStatsCoordinator
**Lines to extract:** ~80 lines
**Location:** `src/SshManager.Terminal/Services/TerminalStatsCoordinator.cs`

**Responsibilities:**
- Orchestrate stats collection lifecycle
- Configure status bar based on connection type
- Handle stats update events
- Manage stats collector instance

**Interface:**
```csharp
public interface ITerminalStatsCoordinator
{
    bool IsCollecting { get; }
    TerminalStats? CurrentStats { get; }

    void StartForSshSession(TerminalSession session, SshTerminalBridge? bridge);
    void StartForSerialSession(TerminalSession session, SerialConnectionInfo? connectionInfo);
    void Stop();
    void Pause();
    void Resume();

    event EventHandler<TerminalStats>? StatsUpdated;
}
```

**Methods to extract from SshTerminalControl:**
- `StartStatsCollection()` (lines 1084-1116)
- `StartSerialStatsCollection()` (lines 1122-1143)
- `StopStatsCollection()` (lines 1145-1152)
- `OnStatsUpdated()` (lines 1154-1158)

---

### 4. ITerminalThemeManager
**Lines to extract:** ~50 lines
**Location:** `src/SshManager.Terminal/Services/TerminalThemeManager.cs`

**Responsibilities:**
- Apply color themes to terminal
- Manage font family and size settings
- Build font stack for WebView2
- Cache current theme state

**Interface:**
```csharp
public interface ITerminalThemeManager
{
    TerminalTheme? CurrentTheme { get; }
    string FontFamily { get; set; }
    double FontSize { get; set; }

    void ApplyTheme(TerminalTheme theme, WebTerminalControl terminal);
    void ApplyFontSettings(WebTerminalControl terminal);
    void Reset();
}
```

**Methods to extract from SshTerminalControl:**
- `ApplyTheme(TerminalTheme theme)` (lines 1586-1610)
- `ApplyFontSettings()` (lines 1612-1621)
- Font property logic (lines 1414-1435)

---

### 5. ITerminalOutputProcessor
**Lines to extract:** ~60 lines
**Location:** `src/SshManager.Terminal/Services/TerminalOutputProcessor.cs`

**Responsibilities:**
- UTF-8 decoding with stateful decoder
- Output buffer management
- Forward processed text to terminal
- Record output for session recording

**Interface:**
```csharp
public interface ITerminalOutputProcessor
{
    int TotalLines { get; }
    int MaxLines { get; set; }
    int MaxLinesInMemory { get; set; }

    string ProcessData(byte[] data);
    void AppendToBuffer(string text);
    string GetAllText();
    void Clear();

    void RecordOutput(ISessionRecorder? recorder, byte[] data);
}
```

**Methods to extract from SshTerminalControl:**
- `DecodeUtf8(byte[] data)` (lines 544-558)
- Output buffer operations (lines 1479-1494)
- `GetOutputText()` (lines 1646-1649)
- `ClearOutputBuffer()` (lines 1665-1668)

---

### 6. ISshSessionConnector
**Lines to extract:** ~180 lines
**Location:** `src/SshManager.Terminal/Services/SshSessionConnector.cs`

**Responsibilities:**
- Handle SSH connection establishment
- Manage SSH-specific bridge wiring
- Handle proxy chain connections
- Process SSH data received events

**Interface:**
```csharp
public interface ISshSessionConnector
{
    Task<SshConnectionResult> ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken cancellationToken = default);

    Task<SshConnectionResult> ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns,
        uint rows,
        CancellationToken cancellationToken = default);

    void WireBridgeEvents(SshTerminalBridge bridge, Action<byte[]> onDataReceived);
    void UnwireBridgeEvents(SshTerminalBridge bridge);

    event EventHandler? Disconnected;
}
```

**Methods to extract from SshTerminalControl:**
- `ConnectAsync()` overloads (lines 598-679)
- `ConnectWithProxyChainAsync()` (lines 684-755)
- `OnSshDataReceived()` (lines 353-374)
- `OnConnectionDisconnected()` (lines 1046-1061)
- `OnBridgeDisconnected()` (lines 1063-1078)

---

### 7. ISerialSessionConnector
**Lines to extract:** ~200 lines
**Location:** `src/SshManager.Terminal/Services/SerialSessionConnector.cs`

**Responsibilities:**
- Handle serial port connection establishment
- Manage serial bridge lifecycle
- Handle serial reconnection
- Process serial data received events

**Interface:**
```csharp
public interface ISerialSessionConnector
{
    ISerialConnectionService? SerialService { get; }
    SerialConnectionInfo? LastConnectionInfo { get; }

    Task ConnectAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        TerminalSession session,
        CancellationToken cancellationToken = default);

    Task ReconnectAsync();

    void WireBridgeEvents(SerialTerminalBridge bridge, Action<byte[]> onDataReceived);
    void UnwireBridgeEvents(SerialTerminalBridge bridge);

    bool CanReconnect { get; }

    event EventHandler? Disconnected;
    event EventHandler? Connected;
}
```

**Methods to extract from SshTerminalControl:**
- `ConnectSerialAsync()` (lines 764-836)
- `ReconnectSerialAsync()` (lines 891-945)
- `OnSerialDataReceived()` (lines 841-858)
- `OnSerialBridgeDisconnected()` (lines 863-886)
- `ShowSerialControls()` / `HideSerialControls()` (lines 1382-1406)

---

### 8. ITerminalSessionManager
**Lines to extract:** ~100 lines
**Location:** `src/SshManager.Terminal/Services/TerminalSessionManager.cs`

**Note:** This may overlap with existing `TerminalSessionManager`. Consider extending existing class.

**Responsibilities:**
- Attach to existing sessions (for split panes)
- Manage session lifecycle (create, attach, detach)
- Track bridge ownership
- Coordinate disconnection cleanup

**Interface:**
```csharp
public interface ITerminalSessionLifecycle
{
    TerminalSession? CurrentSession { get; }
    bool OwnsBridge { get; }
    bool IsConnected { get; }

    Task AttachToSessionAsync(TerminalSession session, WebTerminalControl terminal);
    void Detach();
    void Disconnect();

    event EventHandler? SessionAttached;
    event EventHandler? SessionDetached;
}
```

**Methods to extract from SshTerminalControl:**
- `AttachToSessionAsync()` (lines 951-996)
- `Disconnect()` (lines 1001-1044)
- Session state tracking logic

---

## Implementation Plan

### Phase 1: Core Infrastructure Services (Week 1)

| Order | Service | Est. Lines | Dependencies | Risk |
|-------|---------|-----------|--------------|------|
| 1.1 | `ITerminalStatusDisplay` | 30 | None | Low |
| 1.2 | `ITerminalOutputProcessor` | 60 | None | Low |
| 1.3 | `ITerminalSearchCoordinator` | 40 | OutputBuffer | Low |

**Deliverable:** 130 lines extracted, control reduced to ~1,655 lines

### Phase 2: Session Connectors (Week 2)

| Order | Service | Est. Lines | Dependencies | Risk |
|-------|---------|-----------|--------------|------|
| 2.1 | `ISshSessionConnector` | 180 | ConnectionHandler | Medium |
| 2.2 | `ISerialSessionConnector` | 200 | SerialSignalController | Medium |

**Deliverable:** 380 lines extracted, control reduced to ~1,275 lines

### Phase 3: Orchestration Services (Week 3)

| Order | Service | Est. Lines | Dependencies | Risk |
|-------|---------|-----------|--------------|------|
| 3.1 | `ITerminalStatsCoordinator` | 80 | StatsCollector | Low |
| 3.2 | `ITerminalThemeManager` | 50 | None | Low |
| 3.3 | `ITerminalSessionLifecycle` | 100 | Connectors | Medium |

**Deliverable:** 230 lines extracted, control reduced to ~1,045 lines

### Phase 4: Event Consolidation & Cleanup (Week 4)

| Order | Task | Est. Lines | Risk |
|-------|------|-----------|------|
| 4.1 | Consolidate event forwarding | 80 | Low |
| 4.2 | Remove dead code | 20 | Low |
| 4.3 | Simplify interface implementations | 50 | Low |

**Deliverable:** Control reduced to ~400-500 lines

---

## Detailed File Structure After Refactoring

```
src/SshManager.Terminal/
├── Controls/
│   ├── SshTerminalControl.xaml
│   ├── SshTerminalControl.xaml.cs          (~400-500 lines, orchestration only)
│   ├── WebTerminalControl.xaml.cs
│   └── TerminalFindOverlay.xaml.cs
│
├── Services/
│   ├── Connection/
│   │   ├── ISshSessionConnector.cs          (interface)
│   │   ├── SshSessionConnector.cs           (~200 lines)
│   │   ├── ISerialSessionConnector.cs       (interface)
│   │   └── SerialSessionConnector.cs        (~220 lines)
│   │
│   ├── Display/
│   │   ├── ITerminalStatusDisplay.cs        (interface)
│   │   ├── TerminalStatusDisplay.cs         (~50 lines)
│   │   ├── ITerminalThemeManager.cs         (interface)
│   │   └── TerminalThemeManager.cs          (~80 lines)
│   │
│   ├── Processing/
│   │   ├── ITerminalOutputProcessor.cs      (interface)
│   │   └── TerminalOutputProcessor.cs       (~100 lines)
│   │
│   ├── Search/
│   │   ├── ITerminalSearchCoordinator.cs    (interface)
│   │   └── TerminalSearchCoordinator.cs     (~80 lines)
│   │
│   ├── Stats/
│   │   ├── ITerminalStatsCoordinator.cs     (interface)
│   │   └── TerminalStatsCoordinator.cs      (~120 lines)
│   │
│   └── Lifecycle/
│       ├── ITerminalSessionLifecycle.cs     (interface)
│       └── TerminalSessionLifecycle.cs      (~150 lines)
│
└── (existing services remain unchanged)
    ├── ITerminalKeyboardHandler.cs
    ├── TerminalKeyboardHandler.cs
    ├── ITerminalClipboardService.cs
    ├── TerminalClipboardService.cs
    ├── ITerminalConnectionHandler.cs
    ├── TerminalConnectionHandler.cs
    ├── ITerminalAutocompletionHandler.cs
    ├── TerminalAutocompletionHandler.cs
    ├── ISerialSignalController.cs
    ├── SerialSignalController.cs
    ├── IAutoReconnectManager.cs
    └── AutoReconnectManager.cs
```

---

## SshTerminalControl After Refactoring

After all extractions, the control will be reduced to:

```csharp
public partial class SshTerminalControl : UserControl,
    IKeyboardHandlerContext,
    IAutocompletionHandlerContext,
    IReconnectContext,
    INotifyPropertyChanged
{
    // === INJECTED SERVICES (all extracted logic) ===
    private readonly ITerminalKeyboardHandler _keyboardHandler;
    private readonly ITerminalClipboardService _clipboardService;
    private readonly ITerminalConnectionHandler _connectionHandler;
    private readonly ITerminalAutocompletionHandler _autocompletionHandler;
    private readonly ISerialSignalController _serialSignalController;
    private readonly IAutoReconnectManager _autoReconnectManager;

    // NEW: Additional extracted services
    private readonly ITerminalStatusDisplay _statusDisplay;
    private readonly ITerminalOutputProcessor _outputProcessor;
    private readonly ITerminalSearchCoordinator _searchCoordinator;
    private readonly ITerminalStatsCoordinator _statsCoordinator;
    private readonly ITerminalThemeManager _themeManager;
    private readonly ISshSessionConnector _sshConnector;
    private readonly ISerialSessionConnector _serialConnector;
    private readonly ITerminalSessionLifecycle _sessionLifecycle;

    // === MINIMAL STATE ===
    private TerminalSession? _session;

    // === CONSTRUCTOR: Wire everything together ===
    public SshTerminalControl(...) { /* DI and event wiring only */ }

    // === PUBLIC API: Thin wrappers delegating to services ===
    public Task ConnectAsync(...) => _sshConnector.ConnectAsync(...);
    public Task ConnectSerialAsync(...) => _serialConnector.ConnectAsync(...);
    public void Disconnect() => _sessionLifecycle.Disconnect();
    public void ApplyTheme(TerminalTheme t) => _themeManager.ApplyTheme(t, TerminalHost);
    public void ShowFindOverlay() => _searchCoordinator.ShowSearch();
    // ... etc

    // === INTERFACE IMPLEMENTATIONS: Delegate to services ===
    // IKeyboardHandlerContext, IAutocompletionHandlerContext, IReconnectContext

    // === PRIVATE: Event wiring and forwarding only ===
    private void OnLoaded(...) { /* Initialize services */ }
    private void OnUnloaded(...) { /* Pause services */ }
    private void OnTerminalReady() { /* Apply initial settings */ }
    private void OnTerminalInputReceived(string input) { /* Forward to bridge */ }
}
```

---

## Testing Strategy

### Unit Tests Required

1. **TerminalStatusDisplayTests**
   - Show/Hide state management
   - Message formatting
   - Progress indicator visibility

2. **TerminalOutputProcessorTests**
   - UTF-8 decoding (multi-byte sequences)
   - Buffer append/clear operations
   - Line counting

3. **TerminalSearchCoordinatorTests**
   - Show/hide coordination
   - Search service initialization
   - Event forwarding

4. **SshSessionConnectorTests**
   - Connection establishment flow
   - Proxy chain handling
   - Bridge event wiring/unwiring
   - Disconnection handling

5. **SerialSessionConnectorTests**
   - Serial connection flow
   - Reconnection logic
   - Bridge lifecycle management

6. **TerminalStatsCoordinatorTests**
   - Stats collection lifecycle
   - SSH vs Serial configuration
   - Event handling

7. **TerminalThemeManagerTests**
   - Theme application
   - Font settings
   - Default values

### Integration Tests

1. **Full SSH connection flow** through all services
2. **Full Serial connection flow** through all services
3. **Session mirroring** (split pane attachment)
4. **Reconnection scenarios** for both SSH and Serial

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking existing functionality | Medium | High | Comprehensive test coverage before refactoring |
| Performance regression from indirection | Low | Medium | Profile after each phase |
| Interface instability | Medium | Medium | Design interfaces carefully, minimize changes after Phase 1 |
| Thread safety issues | Medium | High | Document threading model for each service |
| DI complexity increase | Low | Low | Use factory pattern where needed |

---

## Migration Guide

### For Existing Code Using SshTerminalControl

**No breaking changes to public API.** All existing methods remain available:

```csharp
// Before and After - identical usage
await terminal.ConnectAsync(sshService, connectionInfo, callback);
await terminal.ConnectSerialAsync(serialService, connectionInfo, session);
terminal.ApplyTheme(theme);
terminal.ShowFindOverlay();
terminal.Disconnect();
```

### For New Code

New code can inject specific services directly for better testability:

```csharp
// Direct service usage (optional)
var themeManager = new TerminalThemeManager();
themeManager.ApplyTheme(theme, terminalHost);
```

---

## Success Metrics

| Metric | Before | Target | Notes |
|--------|--------|--------|-------|
| Lines of code | 1,785 | 400-500 | ~75% reduction |
| Cyclomatic complexity | High | Low | Each service has single responsibility |
| Test coverage | Unknown | >80% | Per extracted service |
| Number of responsibilities | 12+ | 1-2 | Orchestration + event forwarding only |

---

## Appendix: Method-to-Service Mapping

| Line Range | Method | Target Service |
|------------|--------|----------------|
| 135-190 | Constructor | SshTerminalControl (simplified) |
| 192-207 | TryInitializeLogger | SshTerminalControl |
| 209-232 | OnLoaded/OnUnloaded | SshTerminalControl |
| 234-248 | OnTerminalReady | SshTerminalControl |
| 250-276 | OnTerminalInputReceived | SshTerminalControl |
| 278-302 | OnTerminalResized | SshTerminalControl |
| 304-328 | OnTerminalFocusChanged | SshTerminalControl |
| 330-337 | OnTerminalDataWritten | SshTerminalControl |
| 353-374 | OnSshDataReceived | ISshSessionConnector |
| 378-386 | UserControl_PreviewKeyDown | SshTerminalControl |
| 388-455 | IKeyboardHandlerContext impl | SshTerminalControl |
| 457-502 | Autocompletion Events | SshTerminalControl |
| 504-540 | Find Overlay | ITerminalSearchCoordinator |
| 542-560 | UTF-8 Decoding | ITerminalOutputProcessor |
| 562-590 | Clipboard Operations | SshTerminalControl (delegates) |
| 592-755 | SSH Connection | ISshSessionConnector |
| 757-945 | Serial Connection | ISerialSessionConnector |
| 947-1078 | Session Management | ITerminalSessionLifecycle |
| 1082-1158 | Stats Collection | ITerminalStatsCoordinator |
| 1162-1245 | Reconnection Properties | SshTerminalControl (delegates) |
| 1248-1263 | Status Overlay | ITerminalStatusDisplay |
| 1266-1407 | Serial Controls | ISerialSessionConnector |
| 1410-1669 | Public Properties/Methods | Split across services |
| 1672-1718 | Interface Implementations | SshTerminalControl |
| 1720-1782 | Service Event Handlers | SshTerminalControl |

---

## Revision History

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2026-01-19 | 1.0 | Claude Code | Initial plan created |
