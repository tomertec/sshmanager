# SshManager Comprehensive Improvement Plan

**Created**: January 20, 2026  
**Status**: Planning  
**Estimated Timeline**: 8-11 weeks

## Overview

This document outlines improvements across four categories:
1. **Foundation & Performance** - Core optimizations and reliability
2. **UI/UX Enhancements** - Visual and interaction improvements
3. **New Features** - Additional functionality
4. **Code Quality** - Architecture and maintainability

---

## Executive Summary

| Phase | Duration | Focus Area |
|-------|----------|------------|
| Phase 1 | Weeks 1-2 | Foundation & Performance |
| Phase 2 | Weeks 3-5 | UI/UX Improvements |
| Phase 3 | Weeks 6-9 | New Features |
| Phase 4 | Weeks 10-11 | Code Quality & Polish |

---

## Phase 1: Foundation & Performance (Weeks 1-2)

### 1.1 Terminal Output Performance Optimization

**Goal**: Reduce latency and improve responsiveness with high-volume terminal output.

**Problem**: WebView2 can struggle with rapid terminal updates, causing UI lag.

**Files to modify**:
- `src/SshManager.Terminal/Services/WebTerminalBridge.cs`
- `src/SshManager.Terminal/Controls/WebTerminalControl.xaml.cs`
- `src/SshManager.Core/Models/AppSettings.cs`

**Implementation**:

1. **Add output batching** in `WebTerminalBridge.cs`:
```csharp
public class TerminalOutputBatcher
{
    private readonly Channel<string> _outputChannel;
    private readonly int _flushIntervalMs;
    private readonly int _maxBatchSize;
    
    // Batch output and flush at 60fps (16ms) or when batch is full
    public async Task ProcessOutputAsync(CancellationToken ct)
    {
        var batch = new StringBuilder(4096);
        var timer = Stopwatch.StartNew();
        
        await foreach (var output in _outputChannel.Reader.ReadAllAsync(ct))
        {
            batch.Append(output);
            
            if (timer.ElapsedMilliseconds >= _flushIntervalMs || batch.Length >= _maxBatchSize)
            {
                await FlushBatchAsync(batch.ToString());
                batch.Clear();
                timer.Restart();
            }
        }
    }
}
```

2. **Add settings** to `AppSettings.cs`:
```csharp
/// <summary>
/// Terminal output flush interval in milliseconds (default: 16ms for 60fps).
/// </summary>
[Range(8, 100)]
public int TerminalOutputFlushIntervalMs { get; set; } = 16;

/// <summary>
/// Maximum batch size in bytes before forcing flush.
/// </summary>
[Range(1024, 65536)]
public int TerminalOutputMaxBatchSize { get; set; } = 8192;
```

3. **Optimize xterm.js write calls** in `WebTerminalControl.xaml.cs`:
```csharp
// Use requestAnimationFrame for smoother rendering
await _webView.ExecuteScriptAsync($@"
    if (!window.pendingOutput) window.pendingOutput = '';
    window.pendingOutput += {JsonSerializer.Serialize(data)};
    if (!window.outputScheduled) {{
        window.outputScheduled = true;
        requestAnimationFrame(() => {{
            term.write(window.pendingOutput);
            window.pendingOutput = '';
            window.outputScheduled = false;
        }});
    }}
");
```

**Settings UI** (`SettingsDialog.xaml`):
- Add "Performance" section under Terminal
- Slider for output flush interval (8-100ms)
- Toggle for "Smooth scrolling" (enables/disables batching)

---

### 1.2 Connection Reliability Improvements

**Goal**: Better reconnection behavior and network resilience.

**Files to modify**:
- `src/SshManager.Terminal/Services/ConnectionRetryPolicy.cs`
- `src/SshManager.Terminal/Services/IAutoReconnectManager.cs`
- `src/SshManager.Terminal/Services/SshConnectionService.cs`
- `src/SshManager.Core/Models/AppSettings.cs`

**Implementation**:

1. **Exponential backoff with jitter** in `ConnectionRetryPolicy.cs`:
```csharp
public class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly int _baseDelayMs;
    private readonly int _maxDelayMs;
    private readonly double _jitterFactor;
    private readonly Random _random = new();
    
    public TimeSpan GetDelay(int attemptNumber)
    {
        var exponentialDelay = Math.Min(
            _baseDelayMs * Math.Pow(2, attemptNumber - 1),
            _maxDelayMs
        );
        
        // Add jitter (0.5x to 1.5x)
        var jitter = 1 + (_random.NextDouble() - 0.5) * _jitterFactor;
        return TimeSpan.FromMilliseconds(exponentialDelay * jitter);
    }
}
```

2. **Network connectivity monitoring**:
```csharp
public interface INetworkMonitor
{
    event EventHandler<NetworkStatusChangedEventArgs>? StatusChanged;
    bool IsNetworkAvailable { get; }
    Task<bool> CanReachHostAsync(string hostname, int port, CancellationToken ct);
}

// Implementation uses NetworkChange.NetworkAvailabilityChanged
// and periodic TCP connect checks
```

3. **Settings additions** to `AppSettings.cs`:
```csharp
/// <summary>
/// Base delay for reconnection attempts in milliseconds.
/// </summary>
public int ReconnectBaseDelayMs { get; set; } = 1000;

/// <summary>
/// Maximum delay between reconnection attempts in milliseconds.
/// </summary>
public int ReconnectMaxDelayMs { get; set; } = 30000;

/// <summary>
/// Monitor network connectivity and auto-reconnect when restored.
/// </summary>
public bool EnableNetworkMonitoring { get; set; } = true;
```

---

### 1.3 Host Status Monitoring Improvements

**Goal**: Faster and more accurate host status with less UI impact.

**Files to modify**:
- `src/SshManager.App/Services/IHostStatusService.cs`
- `src/SshManager.App/Services/HostStatusService.cs`
- `src/SshManager.Core/Models/HostStatus.cs`

**Implementation**:

1. **Parallel ping with TCP port check**:
```csharp
public class EnhancedHostStatusService : IHostStatusService
{
    private readonly SemaphoreSlim _concurrencyLimiter;
    
    public async Task RefreshStatusesAsync(IEnumerable<HostEntry> hosts, CancellationToken ct)
    {
        var tasks = hosts.Select(async host =>
        {
            await _concurrencyLimiter.WaitAsync(ct);
            try
            {
                var status = new HostStatus
                {
                    HostId = host.Id,
                    PingLatencyMs = await PingAsync(host.Hostname, ct),
                    IsPortOpen = await CheckPortAsync(host.Hostname, host.Port, ct),
                    LastChecked = DateTimeOffset.UtcNow
                };
                
                // Raise individual status change (not full refresh)
                OnHostStatusChanged(host.Id, status);
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        });
        
        await Task.WhenAll(tasks);
    }
}
```

2. **Incremental UI updates** - Change from dictionary rebind to individual property notifications

3. **Add to HostStatus model**:
```csharp
public class HostStatus
{
    public Guid HostId { get; set; }
    public int? PingLatencyMs { get; set; }
    public bool IsPortOpen { get; set; }
    public bool IsReachable => PingLatencyMs.HasValue || IsPortOpen;
    public DateTimeOffset LastChecked { get; set; }
    public HostStatusLevel Level => CalculateLevel();
}

public enum HostStatusLevel { Unknown, Offline, Degraded, Online }
```

---

### 1.4 SFTP Transfer Improvements

**Goal**: Better reliability and visibility for file transfers.

**Files to modify**:
- `src/SshManager.Terminal/Services/SftpService.cs`
- `src/SshManager.App/ViewModels/SftpBrowserViewModel.cs`
- `src/SshManager.App/Views/Controls/TransferProgressControl.xaml`

**Implementation**:

1. **Resume capability** for large files:
```csharp
public async Task ResumeUploadAsync(string localPath, string remotePath, 
    long startOffset, IProgress<TransferProgress> progress, CancellationToken ct)
{
    using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
    fileStream.Seek(startOffset, SeekOrigin.Begin);
    
    using var sftpStream = _sftpClient.Open(remotePath, FileMode.Append, FileAccess.Write);
    
    var buffer = new byte[81920];
    var totalTransferred = startOffset;
    var totalSize = new FileInfo(localPath).Length;
    
    int bytesRead;
    while ((bytesRead = await fileStream.ReadAsync(buffer, ct)) > 0)
    {
        await sftpStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        totalTransferred += bytesRead;
        progress.Report(new TransferProgress(totalTransferred, totalSize));
    }
}
```

2. **Transfer statistics** in progress UI:
```csharp
public record TransferProgress(
    long BytesTransferred,
    long TotalBytes,
    TimeSpan Elapsed,
    double SpeedBytesPerSecond,
    TimeSpan EstimatedRemaining
);
```

---

## Phase 2: UI/UX Improvements (Weeks 3-5)

### 2.1 Host List Enhancements

**Goal**: Better information density and visual differentiation.

**Files to modify**:
- `src/SshManager.App/Views/Windows/MainWindow.xaml` (lines 473-762)
- `src/SshManager.App/Converters/` (new converters)
- `src/SshManager.Core/Models/HostEntry.cs`
- `src/SshManager.Data/Repositories/ConnectionHistoryRepository.cs`

**Implementation**:

1. **Add connection statistics to HostEntry** (computed property):
```csharp
// In HostEntry.cs - not persisted, loaded separately
[NotMapped]
public HostConnectionStats? ConnectionStats { get; set; }

public record HostConnectionStats(
    DateTimeOffset? LastConnected,
    int TotalConnections,
    int SuccessfulConnections,
    double SuccessRate
);
```

2. **New host card layout features**:
- Connection type icon (SSH vs Serial port)
- Last connected timestamp
- Success rate indicator
- Tags display in card

3. **Add favorite/pinned hosts**:
```csharp
// In HostEntry.cs
public bool IsFavorite { get; set; }
public int SortOrder { get; set; }
```

4. **View mode toggle** (Compact/Normal/Detailed):
```csharp
public enum HostListViewMode { Compact, Normal, Detailed }

// In HostManagementViewModel
[ObservableProperty]
private HostListViewMode _viewMode = HostListViewMode.Normal;
```

---

### 2.2 Session Tab Improvements

**Goal**: Better visual feedback and tab management.

**Files to modify**:
- `src/SshManager.App/Views/Windows/MainWindow.xaml` (lines 822-936)
- `src/SshManager.Terminal/TerminalSession.cs`
- `src/SshManager.App/Behaviors/` (new behavior)

**Implementation**:

1. **Enhanced session tab features**:
- Session duration timer display
- Group color coding on tabs
- Broadcast mode indicator
- Unsaved output indicator

2. **Session duration tracking** in `TerminalSession.cs`:
```csharp
public partial class TerminalSession
{
    private readonly Stopwatch _durationTimer = new();
    
    public TimeSpan Duration => _durationTimer.Elapsed;
    
    public string DurationFormatted => Duration.TotalHours >= 1 
        ? Duration.ToString(@"h\:mm\:ss") 
        : Duration.ToString(@"m\:ss");
}
```

3. **Tab drag-drop reordering** - New behavior class

4. **Middle-click to close** tab support

---

### 2.3 Settings Dialog Reorganization

**Goal**: Better organization with tabbed navigation.

**Files to create/modify**:
- `src/SshManager.App/Views/Dialogs/SettingsDialog.xaml` (major refactor)
- `src/SshManager.App/Views/Controls/Settings/` (new folder)

**New Structure**:
```
Views/Controls/Settings/
├── TerminalSettingsPage.xaml
├── ConnectionSettingsPage.xaml
├── SecuritySettingsPage.xaml
├── AppearanceSettingsPage.xaml
├── BackupSyncSettingsPage.xaml
└── AdvancedSettingsPage.xaml
```

**Navigation categories**:
- Terminal
- Connection
- Security
- Appearance
- Backup & Sync
- Advanced

---

### 2.4 Quick Connect Improvements

**Goal**: Faster host access with better search.

**Files to modify**:
- `src/SshManager.App/Views/Controls/QuickConnectOverlay.xaml`
- `src/SshManager.App/ViewModels/QuickConnectOverlayViewModel.cs`

**Implementation**:

1. **Fuzzy search with highlighting**:
```csharp
public class FuzzyMatcher
{
    public static (bool IsMatch, int Score, List<int> MatchedIndices) Match(string pattern, string text)
    {
        var indices = new List<int>();
        var patternIndex = 0;
        var score = 0;
        
        for (int i = 0; i < text.Length && patternIndex < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(pattern[patternIndex]))
            {
                indices.Add(i);
                score += (i == 0 || !char.IsLetterOrDigit(text[i - 1])) ? 10 : 1;
                patternIndex++;
            }
        }
        
        return (patternIndex == pattern.Length, score, indices);
    }
}
```

2. **Enhanced overlay features**:
- Recent connections section
- Host status indicators in results
- Keyboard navigation (arrow keys)
- Quick action hints at bottom

---

### 2.5 Theme Support (Light/Dark/System)

**Goal**: Add light theme option and system theme following.

**Files to modify**:
- `src/SshManager.App/App.xaml`
- `src/SshManager.App/App.xaml.cs`
- `src/SshManager.App/Services/ThemeService.cs` (new)
- `src/SshManager.Core/Models/AppSettings.cs`

**Implementation**:

1. **Theme service**:
```csharp
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void SetTheme(AppTheme theme);
    event EventHandler<AppTheme>? ThemeChanged;
}

public enum AppTheme { Light, Dark, System }
```

2. **System theme detection** using Windows Registry

3. **Settings update**:
```csharp
// In AppSettings.cs - update existing property
public string Theme { get; set; } = "System"; // Changed default from "Dark"
```

---

## Phase 3: New Features (Weeks 6-9)

### 3.1 Command Palette (Ctrl+Shift+P)

**Goal**: Quick access to all application commands.

**Files to create**:
- `src/SshManager.App/Views/Controls/CommandPalette.xaml`
- `src/SshManager.App/ViewModels/CommandPaletteViewModel.cs`
- `src/SshManager.App/Services/CommandRegistry.cs`

**Implementation**:

1. **Command registry**:
```csharp
public interface ICommandRegistry
{
    void RegisterCommand(AppCommand command);
    IReadOnlyList<AppCommand> GetAllCommands();
    IReadOnlyList<AppCommand> Search(string query);
}

public record AppCommand(
    string Id,
    string Title,
    string Category,
    string? Shortcut,
    Func<Task> ExecuteAsync,
    Func<bool>? CanExecute = null
);
```

2. **Default commands to register**:
- `host.add` - Add New Host (Ctrl+N)
- `host.connect` - Connect to Selected Host (Enter)
- `session.close` - Close Current Session (Ctrl+F4)
- `app.settings` - Open Settings (Ctrl+,)
- `sftp.open` - Open SFTP Browser
- `tools.snippets` - Manage Snippets
- `tools.recordings` - Session Recordings
- `view.split.horizontal` - Split Pane Horizontal
- `view.split.vertical` - Split Pane Vertical

3. **Keyboard shortcut**: Ctrl+Shift+P

---

### 3.2 Multi-Session Commands

**Goal**: Execute commands across multiple hosts simultaneously.

**Files to create**:
- `src/SshManager.App/Views/Dialogs/MultiSessionCommandDialog.xaml`
- `src/SshManager.App/ViewModels/MultiSessionCommandViewModel.cs`
- `src/SshManager.Terminal/Services/IMultiSessionExecutor.cs`

**Implementation**:

1. **Multi-session executor service**:
```csharp
public interface IMultiSessionExecutor
{
    Task<MultiSessionResult> ExecuteCommandAsync(
        IEnumerable<TerminalSession> sessions,
        string command,
        TimeSpan timeout,
        IProgress<MultiSessionProgress> progress,
        CancellationToken ct);
}

public record MultiSessionResult(
    IReadOnlyList<SessionCommandResult> Results,
    int SuccessCount,
    int FailureCount,
    TimeSpan TotalDuration
);

public record SessionCommandResult(
    TerminalSession Session,
    string Output,
    bool IsSuccess,
    TimeSpan Duration,
    string? ErrorMessage
);
```

2. **Dialog features**:
- Session selection checkboxes
- Command input with history
- Tabbed results view per session
- Success/failure indicators
- Export to CSV/JSON

---

### 3.3 SSH Tunnels Dashboard

**Goal**: Visual management of active port forwards.

**Files to create**:
- `src/SshManager.App/Views/Controls/TunnelsDashboard.xaml`
- `src/SshManager.App/ViewModels/TunnelsDashboardViewModel.cs`

**Features**:
- Real-time tunnel status display
- Traffic indicators per tunnel
- One-click enable/disable toggle
- Quick tunnel creation
- Type indicators (Local/Remote/Dynamic)

---

### 3.4 Session Templates

**Goal**: Save and restore session layouts.

**Files to create**:
- `src/SshManager.Core/Models/SessionTemplate.cs`
- `src/SshManager.Data/Repositories/ISessionTemplateRepository.cs`
- `src/SshManager.App/ViewModels/SessionTemplateViewModel.cs`

**Implementation**:

1. **Data model**:
```csharp
public class SessionTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    
    // Serialized pane layout (JSON)
    public string LayoutJson { get; set; } = "";
    
    // Host IDs to connect
    public List<Guid> HostIds { get; set; } = new();
    
    // Startup commands per host
    public Dictionary<Guid, List<string>> StartupCommands { get; set; } = new();
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

2. **Features**:
- Save current layout as template
- Load template and auto-connect
- Execute startup commands
- Export/import templates

---

## Phase 4: Code Quality & Polish (Weeks 10-11)

### 4.1 Large File Refactoring

**MainWindow.xaml (1374 lines)**

Extract into separate UserControls:
```
Views/Controls/
├── HostListPanel.xaml              (lines 171-470 - left panel)
├── SessionTabStrip.xaml            (lines 822-936 - tab bar)  
├── StatusBar.xaml                  (footer area)
└── ToolbarPanel.xaml               (if adding toolbar)
```

**MainWindow.xaml.cs (1167 lines)**

Extract services:
```
Services/
├── KeyboardShortcutHandler.cs      (keyboard handling logic)
├── WindowStateManager.cs           (position/size persistence)
└── PaneOrchestrator.cs             (pane management delegation)
```

**HostEditDialog.xaml (900+ lines)**

Split into section controls:
```
Views/Controls/HostEdit/
├── SshConnectionSection.xaml
├── SerialConnectionSection.xaml
├── AuthenticationSection.xaml
├── GroupAndTagsSection.xaml
└── NotesSection.xaml
```

---

### 4.2 Pattern Improvements

1. **Remove static service locator usage**:
   - Find and replace `App.GetService<T>()` calls with constructor injection
   - Affected locations: some code-behind files

2. **Add comprehensive cancellation token support**:
   - Audit all async methods
   - Add `CancellationToken ct = default` parameter where missing

3. **Implement Result pattern for error handling**:
```csharp
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
```

---

### 4.3 Testing Improvements

1. **Add integration tests** for critical paths:
```
tests/
├── SshManager.Terminal.Tests/
│   ├── SshConnectionServiceTests.cs
│   ├── PortForwardingServiceTests.cs
│   └── SftpServiceTests.cs
├── SshManager.Data.Tests/
│   └── RepositoryTests.cs
└── SshManager.App.Tests/
    └── ViewModelTests.cs
```

2. **Add UI automation tests** using FlaUI or similar framework

3. **Add performance benchmarks** for:
   - Large host list rendering (500+ hosts)
   - Terminal output throughput
   - SFTP transfer speeds

---

### 4.4 Documentation Updates

**Update existing docs**:
- `docs/GETTING_STARTED.md` - Add new features walkthrough
- `docs/ARCHITECTURE.md` - Document new services and patterns
- `docs/API.md` - Document new public APIs

**Create new docs**:
- `docs/KEYBOARD_SHORTCUTS.md` - Complete shortcut reference
- `docs/THEMES.md` - Theme customization guide
- `docs/TEMPLATES.md` - Session templates guide

---

## Implementation Priority Matrix

| Feature | Impact | Effort | Priority |
|---------|--------|--------|----------|
| Terminal performance optimization | High | Medium | **P1** |
| Host list visual improvements | High | Low | **P1** |
| Settings dialog reorganization | Medium | Medium | **P2** |
| Command palette | Medium | Low | **P2** |
| Multi-session commands | High | High | **P2** |
| Theme support | Medium | Low | **P3** |
| SSH tunnels dashboard | Medium | Medium | **P3** |
| Session templates | Low | Medium | **P4** |

---

## Risk Areas

1. **WebView2 performance**: May require alternative approaches if batching doesn't help sufficiently
2. **Settings migration**: Need careful handling of existing user settings during reorganization
3. **Large refactors**: MainWindow split may introduce regressions - thorough testing required

---

## Compatibility Notes

- All changes should maintain backward compatibility with existing databases
- Settings migrations should preserve user preferences
- Theme changes should not affect terminal color schemes (separate concern)
- Database schema changes require EF Core migrations

---

## Success Metrics

1. **Performance**:
   - Terminal output latency < 50ms for 10KB/s throughput
   - Host list renders 500 hosts in < 500ms
   - Application startup time < 3 seconds

2. **Usability**:
   - Settings findable within 2 clicks
   - Command palette response time < 100ms
   - Quick connect search returns results in < 50ms

3. **Reliability**:
   - Auto-reconnect succeeds > 95% of time when network restored
   - SFTP resume works for files > 100MB
   - No data loss on unexpected shutdown

---

## Appendix: File Summary

### Files to Create
- `src/SshManager.App/Services/ThemeService.cs`
- `src/SshManager.App/Services/CommandRegistry.cs`
- `src/SshManager.App/Services/KeyboardShortcutHandler.cs`
- `src/SshManager.App/Views/Controls/CommandPalette.xaml`
- `src/SshManager.App/Views/Controls/TunnelsDashboard.xaml`
- `src/SshManager.App/Views/Controls/Settings/*.xaml` (6 files)
- `src/SshManager.App/Views/Dialogs/MultiSessionCommandDialog.xaml`
- `src/SshManager.Core/Models/SessionTemplate.cs`
- `src/SshManager.Terminal/Services/IMultiSessionExecutor.cs`

### Files to Modify (Major Changes)
- `src/SshManager.App/Views/Windows/MainWindow.xaml`
- `src/SshManager.App/Views/Windows/MainWindow.xaml.cs`
- `src/SshManager.App/Views/Dialogs/SettingsDialog.xaml`
- `src/SshManager.App/Views/Dialogs/HostEditDialog.xaml`
- `src/SshManager.App/Views/Controls/QuickConnectOverlay.xaml`
- `src/SshManager.Terminal/Services/WebTerminalBridge.cs`
- `src/SshManager.Terminal/Services/ConnectionRetryPolicy.cs`
- `src/SshManager.Core/Models/AppSettings.cs`
- `src/SshManager.Core/Models/HostEntry.cs`

---

*This plan is a living document and should be updated as implementation progresses.*
