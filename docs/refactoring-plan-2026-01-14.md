# Refactoring Plan: SshTerminalControl and SshConnectionService

**Date:** 2026-01-14
**Target Files:**
- `src/SshManager.Terminal/Controls/SshTerminalControl.xaml.cs` (978 lines)
- `src/SshManager.Terminal/Services/SshConnectionService.cs` (959 lines)

---

## Executive Summary

This plan outlines the refactoring strategy for the two largest files in the SshManager codebase. Both files exhibit code smells including God Object pattern, multiple responsibilities, and violation of Single Responsibility Principle. The refactoring will improve maintainability, testability, and adherence to SOLID principles.

---

## Part 1: SshTerminalControl.xaml.cs Refactoring

### Current State Analysis

**File:** 978 lines
**Location:** `src/SshManager.Terminal/Controls/SshTerminalControl.xaml.cs`

#### Identified Responsibilities (7 concerns in one class):

| Responsibility | Lines | Description |
|----------------|-------|-------------|
| Connection Management | ~170 | `ConnectAsync`, `ConnectWithProxyChainAsync`, `AttachToSessionAsync`, `Disconnect` |
| Keyboard Handling | ~80 | `UserControl_PreviewKeyDown` with Delete, Insert, Ctrl+F, Escape, copy/paste, zoom |
| Find Overlay | ~40 | `ShowFindOverlay`, `HideFindOverlay`, search event handlers |
| Clipboard Operations | ~45 | `CopyToClipboard`, `PasteFromClipboard` |
| Stats Tracking | ~55 | `StatsTimer_Tick`, throughput calculation, server stats collection |
| Status Overlay | ~15 | `ShowStatus`, `HideStatus` |
| Font/Theme Configuration | ~80 | `ApplyTheme`, `ApplyFontSettings`, `BuildFontStack`, `QuoteIfNeeded` |

#### Code Smells:

1. **God Object** - Control handles 7+ distinct responsibilities
2. **Long Methods** - `ConnectAsync` and `ConnectWithProxyChainAsync` have ~80% code duplication
3. **Mixed Concerns** - UI logic interleaved with SSH connection logic
4. **Scattered State** - Multiple fields for different concerns mixed together

### Proposed Extraction Plan

#### 1.1 Extract `TerminalKeyboardHandler`

**New File:** `src/SshManager.Terminal/Services/TerminalKeyboardHandler.cs`

```csharp
public interface ITerminalKeyboardHandler
{
    bool HandleKeyDown(KeyEventArgs e);
}

public class TerminalKeyboardHandler : ITerminalKeyboardHandler
{
    private readonly ISshTerminalBridge _bridge;
    private readonly IWebTerminalControl _terminalHost;
    private readonly IFindOverlay _findOverlay;
    private readonly IClipboardService _clipboardService;

    // Handle all keyboard shortcuts: Delete, Insert, Ctrl+F, Escape,
    // Ctrl+Shift+C/V, Ctrl++/-, Ctrl+0
}
```

**Benefits:**
- Isolates keyboard handling logic
- Makes shortcut handling testable
- Allows easy addition of new shortcuts

#### 1.2 Extract `TerminalClipboardService`

**New File:** `src/SshManager.Terminal/Services/TerminalClipboardService.cs`

```csharp
public interface ITerminalClipboardService
{
    void CopyToClipboard();
    void PasteFromClipboard(Action<string> sendText);
}

public class TerminalClipboardService : ITerminalClipboardService
{
    private readonly ILogger<TerminalClipboardService> _logger;

    // Encapsulates clipboard operations with proper error handling
}
```

**Benefits:**
- Clipboard operations become testable (mockable)
- Can be reused by other controls
- Simplifies error handling

#### 1.3 Extract `TerminalStatsCollector`

**New File:** `src/SshManager.Terminal/Services/TerminalStatsCollector.cs`

```csharp
public interface ITerminalStatsCollector : IDisposable
{
    void Start(TerminalSession session, ISshTerminalBridge bridge);
    void Stop();
    event EventHandler<SessionStats>? StatsUpdated;
}

public class TerminalStatsCollector : ITerminalStatsCollector
{
    private readonly IServerStatsService? _serverStatsService;
    private readonly DispatcherTimer _timer;

    // Encapsulates all stats collection logic
    // - Uptime tracking
    // - Throughput calculation (bytes/sec)
    // - Server stats (CPU, memory, disk) every 10 seconds
}
```

**Benefits:**
- Stats logic becomes independently testable
- Timer lifecycle management in one place
- Easy to modify collection intervals

#### 1.4 Extract `TerminalConnectionHandler`

**New File:** `src/SshManager.Terminal/Services/TerminalConnectionHandler.cs`

```csharp
public interface ITerminalConnectionHandler
{
    Task<ConnectionResult> ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns, uint rows,
        CancellationToken ct);

    Task<ConnectionResult> ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> chain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        uint columns, uint rows,
        CancellationToken ct);

    void Disconnect();
}

public record ConnectionResult(
    ISshConnection Connection,
    SshTerminalBridge Bridge);
```

**Benefits:**
- Eliminates code duplication between direct and proxy chain connections
- Connection logic becomes testable
- Clear separation from UI concerns

#### 1.5 Extract `FontStackBuilder` (Static Utility)

**New File:** `src/SshManager.Terminal/Utilities/FontStackBuilder.cs`

```csharp
public static class FontStackBuilder
{
    public static string Build(string preferredFont, string[] fallbacks);
    internal static string QuoteIfNeeded(string font);
}
```

**Benefits:**
- Reusable font utilities
- Easy to unit test
- Removes clutter from main control

### Refactored SshTerminalControl Structure

After extraction, the control should be ~350-400 lines with clear responsibilities:

```csharp
public partial class SshTerminalControl : UserControl
{
    // Dependencies (injected or set via properties)
    private readonly ITerminalKeyboardHandler _keyboardHandler;
    private readonly ITerminalClipboardService _clipboardService;
    private readonly ITerminalStatsCollector _statsCollector;
    private readonly ITerminalConnectionHandler _connectionHandler;

    // Core state
    private TerminalSession? _session;
    private SshTerminalBridge? _bridge;
    private TerminalTheme? _currentTheme;

    // Lifecycle
    public SshTerminalControl() { /* Wire up events */ }
    private void OnLoaded(object sender, RoutedEventArgs e) { }
    private void OnUnloaded(object sender, RoutedEventArgs e) { }

    // Terminal events (delegation)
    private void OnTerminalReady() { }
    private void OnTerminalInputReceived(string input) { }
    private void OnTerminalResized(int cols, int rows) { }

    // Public API
    public Task ConnectAsync(...) => _connectionHandler.ConnectAsync(...);
    public void Disconnect() => _connectionHandler.Disconnect();
    public void ApplyTheme(TerminalTheme theme) { }
    public void FocusInput() => TerminalHost.Focus();
}
```

---

## Part 2: SshConnectionService.cs Refactoring

### Current State Analysis

**File:** 959 lines (contains 3 classes)
**Location:** `src/SshManager.Terminal/Services/SshConnectionService.cs`

#### Classes in File:

| Class | Lines | Description |
|-------|-------|-------------|
| `SshConnectionService` | ~610 | Main service for establishing connections |
| `SshConnection` | ~145 | Wrapper for direct SSH connections |
| `ProxyChainSshConnection` | ~195 | Wrapper for proxy chain connections |

#### Code Smells:

1. **Multiple Classes in One File** - Violates one-class-per-file principle
2. **Code Duplication** - `SshConnection` and `ProxyChainSshConnection` share ~70% identical code:
   - `TrackDisposable` method
   - `ResizeTerminal` method
   - `RunCommandAsync` method
   - `Dispose` pattern (event unsubscribe, stream dispose, client dispose, tracked disposables)
3. **Long Method** - `ConnectWithProxyChainAsync` is ~185 lines

### Proposed Extraction Plan

#### 2.1 Extract Base Class `SshConnectionBase`

**New File:** `src/SshManager.Terminal/Services/SshConnectionBase.cs`

```csharp
public abstract class SshConnectionBase : ISshConnection
{
    protected readonly SshClient Client;
    protected readonly ILogger Logger;
    protected readonly ITerminalResizeService ResizeService;
    protected readonly List<IDisposable> Disposables = new();
    protected bool Disposed;

    public ShellStream ShellStream { get; }
    public bool IsConnected => Client.IsConnected && !Disposed;
    public event EventHandler? Disconnected;

    protected SshConnectionBase(
        SshClient client,
        ShellStream shellStream,
        ILogger logger,
        ITerminalResizeService resizeService)
    {
        Client = client;
        ShellStream = shellStream;
        Logger = logger;
        ResizeService = resizeService;

        Client.ErrorOccurred += OnError;
        ShellStream.Closed += OnStreamClosed;
    }

    public void TrackDisposable(IDisposable disposable) => Disposables.Add(disposable);

    public bool ResizeTerminal(uint columns, uint rows)
        => ResizeService.TryResize(ShellStream, columns, rows);

    public async Task<string?> RunCommandAsync(string command, TimeSpan? timeout = null)
    {
        // Shared implementation
    }

    protected virtual void OnError(object? sender, ExceptionEventArgs e)
    {
        Logger.LogWarning(e.Exception, "SSH connection error occurred");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnStreamClosed(object? sender, EventArgs e)
    {
        Logger.LogInformation("SSH shell stream closed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void DisposeCore()
    {
        // Common dispose logic: unsubscribe events, dispose stream, dispose client, dispose tracked
    }

    public void Dispose() { /* Template method calling DisposeCore */ }
    public async ValueTask DisposeAsync() => await Task.Run(Dispose);
}
```

#### 2.2 Simplify `SshConnection`

**New File:** `src/SshManager.Terminal/Services/SshConnection.cs`

```csharp
internal sealed class SshConnection : SshConnectionBase
{
    public SshConnection(
        SshClient client,
        ShellStream shellStream,
        ILogger logger,
        ITerminalResizeService resizeService)
        : base(client, shellStream, logger, resizeService)
    {
    }

    // No additional logic needed - base class handles everything
}
```

**Result:** ~15 lines (down from ~145)

#### 2.3 Simplify `ProxyChainSshConnection`

**New File:** `src/SshManager.Terminal/Services/ProxyChainSshConnection.cs`

```csharp
internal sealed class ProxyChainSshConnection : SshConnectionBase
{
    private readonly IReadOnlyList<SshClient> _intermediateClients;
    private readonly IReadOnlyList<ForwardedPortLocal> _forwardedPorts;

    public ProxyChainSshConnection(
        SshClient targetClient,
        ShellStream shellStream,
        IReadOnlyList<SshClient> intermediateClients,
        IReadOnlyList<ForwardedPortLocal> forwardedPorts,
        ILogger logger,
        ITerminalResizeService resizeService)
        : base(targetClient, shellStream, logger, resizeService)
    {
        _intermediateClients = intermediateClients;
        _forwardedPorts = forwardedPorts;

        foreach (var client in intermediateClients)
        {
            client.ErrorOccurred += OnIntermediateError;
        }
    }

    private void OnIntermediateError(object? sender, ExceptionEventArgs e)
    {
        Logger.LogWarning(e.Exception, "Proxy chain intermediate connection error");
        RaiseDisconnected();
    }

    protected override void DisposeCore()
    {
        // Unsubscribe intermediate clients
        // Stop and dispose forwarded ports (reverse order)
        // Dispose intermediate clients (reverse order)
        base.DisposeCore();
    }
}
```

**Result:** ~60 lines (down from ~195)

#### 2.4 Extract `ProxyChainConnectionBuilder`

**New File:** `src/SshManager.Terminal/Services/ProxyChainConnectionBuilder.cs`

```csharp
public interface IProxyChainConnectionBuilder
{
    Task<ProxyChainBuildResult> BuildChainAsync(
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback,
        CancellationToken ct);
}

public record ProxyChainBuildResult(
    SshClient TargetClient,
    int FinalLocalPort,
    IReadOnlyList<SshClient> IntermediateClients,
    IReadOnlyList<ForwardedPortLocal> ForwardedPorts,
    IReadOnlyList<IDisposable> Disposables);

public class ProxyChainConnectionBuilder : IProxyChainConnectionBuilder
{
    private readonly ISshAuthenticationFactory _authFactory;
    private readonly ILogger<ProxyChainConnectionBuilder> _logger;

    // Encapsulates all the proxy chain building logic:
    // - Iterating through hops
    // - Creating auth methods for each hop
    // - Setting up local port forwards
    // - Host key verification per hop
}
```

**Benefits:**
- `ConnectWithProxyChainAsync` in main service becomes ~40 lines
- Chain building logic is testable independently
- Clear separation of connection establishment vs chain building

#### 2.5 Extract `AlgorithmConfigurator`

**New File:** `src/SshManager.Terminal/Services/AlgorithmConfigurator.cs`

```csharp
public static class AlgorithmConfigurator
{
    public static void ConfigureAlgorithms(ConnectionInfo connInfo, ILogger? logger = null);
    internal static void ReorderAlgorithms<T>(IDictionary<string, T> algorithms, string[] preferredOrder);
}
```

**Benefits:**
- Algorithm configuration is reusable
- Easy to update algorithm preferences
- Removes configuration clutter from main service

### Refactored SshConnectionService Structure

After extraction, the service should be ~250-300 lines:

```csharp
public sealed class SshConnectionService : ISshConnectionService
{
    private readonly ISshAuthenticationFactory _authFactory;
    private readonly IProxyChainConnectionBuilder _proxyChainBuilder;
    private readonly ITerminalResizeService _resizeService;
    private readonly ILogger<SshConnectionService> _logger;

    public async Task<ISshConnection> ConnectAsync(...)
    {
        // Direct connection logic (~100 lines)
    }

    public async Task<ISshConnection> ConnectWithProxyChainAsync(...)
    {
        // Delegates to _proxyChainBuilder, then creates ProxyChainSshConnection
        // (~40 lines)
    }
}
```

---

## Part 3: Implementation Phases

### Phase 1: SshConnectionService Refactoring (Lower Risk)

**Rationale:** Start with service layer - no UI dependencies, easier to test.

| Step | Task | Estimated Effort |
|------|------|------------------|
| 1.1 | Create `SshConnectionBase` abstract class | Medium |
| 1.2 | Extract `SshConnection` to own file, inherit from base | Low |
| 1.3 | Extract `ProxyChainSshConnection` to own file, inherit from base | Low |
| 1.4 | Create `AlgorithmConfigurator` static class | Low |
| 1.5 | Create `IProxyChainConnectionBuilder` and implementation | Medium |
| 1.6 | Refactor `SshConnectionService` to use builder | Medium |
| 1.7 | Write/update unit tests | Medium |

**Deliverable:** 6 new files, original file reduced to ~250-300 lines

### Phase 2: SshTerminalControl Refactoring (Higher Risk)

**Rationale:** UI control with more integration points - refactor after services are stable.

| Step | Task | Estimated Effort |
|------|------|------------------|
| 2.1 | Create `ITerminalClipboardService` and implementation | Low |
| 2.2 | Create `FontStackBuilder` static utility | Low |
| 2.3 | Create `ITerminalStatsCollector` and implementation | Medium |
| 2.4 | Create `ITerminalKeyboardHandler` and implementation | Medium |
| 2.5 | Create `ITerminalConnectionHandler` and implementation | High |
| 2.6 | Refactor `SshTerminalControl` to use extracted services | High |
| 2.7 | Write/update integration tests | Medium |

**Deliverable:** 5 new files, control reduced to ~350-400 lines

---

## Part 4: File Structure After Refactoring

### New Files to Create

```
src/SshManager.Terminal/
├── Services/
│   ├── SshConnectionService.cs          (refactored: ~250-300 lines)
│   ├── SshConnectionBase.cs             (NEW: ~120 lines)
│   ├── SshConnection.cs                 (NEW: ~15 lines)
│   ├── ProxyChainSshConnection.cs       (NEW: ~60 lines)
│   ├── ProxyChainConnectionBuilder.cs   (NEW: ~150 lines)
│   ├── AlgorithmConfigurator.cs         (NEW: ~50 lines)
│   ├── TerminalClipboardService.cs      (NEW: ~50 lines)
│   ├── TerminalStatsCollector.cs        (NEW: ~100 lines)
│   ├── TerminalKeyboardHandler.cs       (NEW: ~120 lines)
│   └── TerminalConnectionHandler.cs     (NEW: ~150 lines)
├── Utilities/
│   └── FontStackBuilder.cs              (NEW: ~40 lines)
└── Controls/
    └── SshTerminalControl.xaml.cs       (refactored: ~350-400 lines)
```

### Interface Additions

```
src/SshManager.Terminal/
└── Services/
    ├── ITerminalClipboardService.cs
    ├── ITerminalStatsCollector.cs
    ├── ITerminalKeyboardHandler.cs
    ├── ITerminalConnectionHandler.cs
    └── IProxyChainConnectionBuilder.cs
```

---

## Part 5: Testing Strategy

### Unit Tests to Add

| Component | Test Focus |
|-----------|------------|
| `SshConnectionBase` | Dispose pattern, event handling, `RunCommandAsync` |
| `ProxyChainConnectionBuilder` | Chain building, cleanup on failure |
| `AlgorithmConfigurator` | Algorithm reordering |
| `TerminalClipboardService` | Error handling scenarios |
| `TerminalStatsCollector` | Stats calculation, timer management |
| `TerminalKeyboardHandler` | All keyboard shortcuts |
| `FontStackBuilder` | Font quoting, fallback stacking |

### Integration Tests to Update

| Test File | Updates Needed |
|-----------|----------------|
| `SshConnectionIntegrationTests.cs` | Verify refactored service behavior unchanged |
| `TerminalFeatureIntegrationTests.cs` | Verify terminal control behavior unchanged |

---

## Part 6: Risk Mitigation

### Potential Risks

| Risk | Mitigation |
|------|------------|
| Breaking existing functionality | Comprehensive test coverage before refactoring |
| Regression in connection handling | Integration tests for all auth types and proxy chains |
| Performance degradation | Profile before/after, minimize allocations |
| DI container changes | Update `App.xaml.cs` registrations incrementally |

### Rollback Strategy

1. Create feature branch for refactoring
2. Make incremental commits per extraction
3. Run full test suite after each phase
4. Keep original code commented until tests pass
5. Delete commented code only after PR approval

---

## Part 7: Success Metrics

| Metric | Before | Target |
|--------|--------|--------|
| `SshTerminalControl.xaml.cs` lines | 978 | 350-400 |
| `SshConnectionService.cs` lines | 959 | 250-300 |
| Classes per file | 3 | 1 |
| Test coverage for connection logic | Low | High |
| Cyclomatic complexity per method | High | <10 |

---

## Appendix: Dependency Injection Updates

Update `App.xaml.cs` to register new services:

```csharp
// Phase 1 additions
services.AddSingleton<IProxyChainConnectionBuilder, ProxyChainConnectionBuilder>();

// Phase 2 additions
services.AddTransient<ITerminalClipboardService, TerminalClipboardService>();
services.AddTransient<ITerminalStatsCollector, TerminalStatsCollector>();
services.AddTransient<ITerminalKeyboardHandler, TerminalKeyboardHandler>();
services.AddTransient<ITerminalConnectionHandler, TerminalConnectionHandler>();
```

---

**Document Version:** 1.0
**Author:** Code Review Agent
**Review Status:** Pending
