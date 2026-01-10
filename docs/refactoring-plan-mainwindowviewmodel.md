# Plan: Split MainWindowViewModel into Focused ViewModels

## Problem
`MainWindowViewModel.cs` has grown to 1,604 lines with 18 constructor dependencies - a significant code smell indicating too many responsibilities.

## Solution Overview
Split into 6 focused ViewModels using the **Facade Pattern** to maintain backward compatibility with existing XAML bindings and code-behind access.

---

## New ViewModel Structure

### 1. HostManagementViewModel (NEW)
**File**: `src/SshManager.App/ViewModels/HostManagementViewModel.cs`

**Responsibility**: Host and group data management

**Dependencies** (6):
- IHostRepository, IGroupRepository, ISettingsRepository
- ISecretProtector, IProxyJumpProfileRepository, IPortForwardingProfileRepository

**Properties**:
- `Hosts`, `Groups`, `SelectedHost`, `SelectedGroupFilter`, `SearchText`, `IsLoading`

**Commands**:
- `AddHostCommand`, `EditHostCommand`, `DeleteHostCommand`, `SaveHostCommand`
- `AddGroupCommand`, `EditGroupCommand`, `DeleteGroupCommand`
- `SearchCommand`, `FilterByGroupCommand`

**Methods**: `LoadDataAsync()`, `RefreshHostsAsync()`

---

### 2. SessionViewModel (NEW)
**File**: `src/SshManager.App/ViewModels/SessionViewModel.cs`

**Responsibility**: Terminal session lifecycle and SSH connections

**Dependencies** (8):
- ITerminalSessionManager, ISshConnectionService, IConnectionHistoryRepository
- ISettingsRepository, ISecretProtector, ICredentialCache
- IHostFingerprintRepository, ILogger

**Properties**:
- `Sessions`, `CurrentSession`, `HasActiveSessions`

**Commands**: `ConnectCommand`, `CloseSessionCommand`

**Methods**:
- `CreateSessionForHostAsync()`, `CreateConnectionInfoAsync()`
- `RecordConnectionResultAsync()`
- `CreateHostKeyVerificationCallback()`, `CreateKeyboardInteractiveCallback()`

**Events**: `SessionCreated`

**Exposed Services**: `SshService`, `FingerprintRepository`, `CredentialCache`

---

### 3. SessionLoggingViewModel (NEW)
**File**: `src/SshManager.App/ViewModels/SessionLoggingViewModel.cs`

**Responsibility**: Session logging controls

**Dependencies** (3):
- ISessionLoggingService, ISettingsRepository, SessionViewModel (for CurrentSession)

**Properties**:
- `IsCurrentSessionLogging`, `CurrentSessionLogPath`
- `CurrentSessionLogLevel`, `CurrentSessionRedactTypedSecrets`
- `AvailableSessionLogLevels`

**Commands**:
- `ToggleSessionLoggingCommand`, `OpenCurrentSessionLogFileCommand`
- `OpenLogDirectoryCommand`, `SetCurrentSessionLogLevelCommand`

---

### 4. BroadcastInputViewModel (NEW)
**File**: `src/SshManager.App/ViewModels/BroadcastInputViewModel.cs`

**Responsibility**: Multi-session broadcast input

**Dependencies** (2):
- ITerminalSessionManager, IBroadcastInputService

**Properties**: `IsBroadcastMode`, `BroadcastSelectedCount`, `BroadcastService`

**Commands**:
- `ToggleBroadcastModeCommand`, `SelectAllForBroadcastCommand`
- `DeselectAllForBroadcastCommand`, `ToggleBroadcastSelectionCommand`

---

### 5. SftpLauncherViewModel (NEW)
**File**: `src/SshManager.App/ViewModels/SftpLauncherViewModel.cs`

**Responsibility**: Launching SFTP browser windows

**Dependencies** (5):
- ISftpService, ISettingsRepository, ISecretProtector
- ICredentialCache, SessionViewModel (for CurrentSession)

**Commands**:
- `OpenSftpBrowserCommand` (current session)
- `OpenSftpBrowserForHostCommand` (from host list)

---

### 6. ImportExportViewModel (NEW)
**File**: `src/SshManager.App/ViewModels/ImportExportViewModel.cs`

**Responsibility**: Host data import/export

**Dependencies** (5):
- IExportImportService, IHostRepository, IGroupRepository
- IProxyJumpProfileRepository, IPortForwardingProfileRepository

**Methods**:
- `ExportHostsAsync()`, `ImportHostsAsync()`
- `ImportFromSshConfigAsync()`, `ImportFromPuttyAsync()`

**Events**: `HostsImported` (triggers HostManagementViewModel refresh)

---

### 7. MainWindowViewModel (REFACTORED - Coordinator)
**File**: `src/SshManager.App/ViewModels/MainWindowViewModel.cs`

**Responsibility**: Coordinate child ViewModels, expose facade API

**Dependencies** (8 - reduced from 18):
- HostManagementViewModel, SessionViewModel, SessionLoggingViewModel
- BroadcastInputViewModel, SftpLauncherViewModel, ImportExportViewModel
- PortForwardingManagerViewModel (existing), IHostStatusService

**Facade Pattern**: Delegate properties/commands to child ViewModels for backward compatibility:
```csharp
public ObservableCollection<HostEntry> Hosts => _hostManagement.Hosts;
public IRelayCommand AddHostCommand => _hostManagement.AddHostCommand;
public event EventHandler<TerminalSession>? SessionCreated
{
    add => _session.SessionCreated += value;
    remove => _session.SessionCreated -= value;
}
```

---

## Implementation Phases

### Phase 1: HostManagementViewModel
1. Create `HostManagementViewModel.cs` with host/group properties and commands
2. Move `LoadDataAsync()`, `RefreshHostsAsync()`, search logic
3. Move CRUD commands: AddHost, EditHost, DeleteHost, SaveHost, AddGroup, EditGroup, DeleteGroup
4. Update MainWindowViewModel to inject and delegate to HostManagementViewModel
5. Add facade properties/commands
6. **Test**: Host list loads, search works, CRUD operations work

### Phase 2: SessionViewModel
1. Create `SessionViewModel.cs` with session management
2. Move `Sessions`, `CurrentSession`, `HasActiveSessions`
3. Move `ConnectCommand`, `CloseSessionCommand`, `CreateSessionForHostAsync()`
4. Move `CreateConnectionInfoAsync()`, `RecordConnectionResultAsync()`
5. Move callback creation methods
6. Expose `SshService`, `FingerprintRepository`, `CredentialCache`
7. Wire `SessionCreated` event
8. Update MainWindowViewModel facade
9. **Test**: Connect to host, close session, multiple sessions work

### Phase 3: SessionLoggingViewModel
1. Create `SessionLoggingViewModel.cs`
2. Inject SessionViewModel, subscribe to `CurrentSession` changes
3. Move logging properties and commands
4. Update MainWindowViewModel facade
5. **Test**: Toggle logging, open log file, change log level

### Phase 4: BroadcastInputViewModel
1. Create `BroadcastInputViewModel.cs`
2. Move broadcast mode properties and commands
3. Update MainWindowViewModel facade
4. **Test**: Toggle broadcast, select/deselect sessions

### Phase 5: SftpLauncherViewModel
1. Create `SftpLauncherViewModel.cs`
2. Inject SessionViewModel for CurrentSession access
3. Move SFTP browser launch commands
4. Update MainWindowViewModel facade
5. **Test**: Open SFTP from toolbar, open SFTP from host list

### Phase 6: ImportExportViewModel
1. Create `ImportExportViewModel.cs`
2. Move export/import methods
3. Add `HostsImported` event, subscribe in HostManagementViewModel
4. Update MainWindowViewModel facade
5. Update MainWindow.xaml.cs to use facade methods
6. **Test**: Export hosts, import from JSON/SSH config/PuTTY

### Phase 7: DI Registration & Cleanup
1. Update `App.xaml.cs` with new ViewModel registrations:
   ```csharp
   services.AddSingleton<HostManagementViewModel>();
   services.AddSingleton<SessionViewModel>();
   services.AddSingleton<SessionLoggingViewModel>();
   services.AddSingleton<BroadcastInputViewModel>();
   services.AddSingleton<SftpLauncherViewModel>();
   services.AddSingleton<ImportExportViewModel>();
   ```
2. Remove dead code from MainWindowViewModel
3. Verify all dependencies resolved correctly

---

## Files to Modify

| File | Changes |
|------|---------|
| `src/SshManager.App/ViewModels/MainWindowViewModel.cs` | Refactor to coordinator with facade |
| `src/SshManager.App/ViewModels/HostManagementViewModel.cs` | NEW |
| `src/SshManager.App/ViewModels/SessionViewModel.cs` | NEW |
| `src/SshManager.App/ViewModels/SessionLoggingViewModel.cs` | NEW |
| `src/SshManager.App/ViewModels/BroadcastInputViewModel.cs` | NEW |
| `src/SshManager.App/ViewModels/SftpLauncherViewModel.cs` | NEW |
| `src/SshManager.App/ViewModels/ImportExportViewModel.cs` | NEW |
| `src/SshManager.App/App.xaml.cs` | Add DI registrations |
| `src/SshManager.App/Views/Windows/MainWindow.xaml.cs` | Update method calls to use facade (minimal) |

---

## Verification Steps

After each phase:
1. `dotnet build SshManager.sln` - Ensure no compilation errors
2. `dotnet run --project src/SshManager.App` - Manual testing:
   - Phase 1: Host list loads, search filters, add/edit/delete hosts and groups
   - Phase 2: Connect to SSH host, terminal works, close session
   - Phase 3: Toggle session logging, open log directory
   - Phase 4: Enable broadcast mode, select sessions
   - Phase 5: Open SFTP browser from toolbar and host list
   - Phase 6: Export hosts to JSON, import from JSON

Final verification:
- All keyboard shortcuts work (Ctrl+N, Ctrl+E, Delete, Enter, F5, Ctrl+W, Ctrl+B)
- Session tabs work correctly
- Port forwarding panel works
- No memory leaks (event subscriptions properly managed)

---

## Success Criteria

- MainWindowViewModel: ~1600 lines → ~200-300 lines
- Constructor dependencies: 18 → 8
- Each child ViewModel: 2-8 dependencies
- All existing functionality preserved
- No breaking changes to XAML bindings

---

## Key Patterns to Follow

### CommunityToolkit.Mvvm Attributes
```csharp
public partial class HostManagementViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<HostEntry> _hosts = [];

    [RelayCommand]
    private async Task AddHostAsync() { ... }
}
```

### Facade Property Pattern
```csharp
// In MainWindowViewModel
public ObservableCollection<HostEntry> Hosts => _hostManagement.Hosts;

// For two-way binding
public HostEntry? SelectedHost
{
    get => _hostManagement.SelectedHost;
    set => _hostManagement.SelectedHost = value;
}
```

### Event Forwarding Pattern
```csharp
public event EventHandler<TerminalSession>? SessionCreated
{
    add => _session.SessionCreated += value;
    remove => _session.SessionCreated -= value;
}
```

### Cross-ViewModel Communication
```csharp
// SessionLoggingViewModel subscribes to SessionViewModel
public SessionLoggingViewModel(SessionViewModel sessionViewModel, ...)
{
    _sessionViewModel = sessionViewModel;
    _sessionViewModel.PropertyChanged += (s, e) =>
    {
        if (e.PropertyName == nameof(SessionViewModel.CurrentSession))
        {
            OnPropertyChanged(nameof(IsCurrentSessionLogging));
            OnPropertyChanged(nameof(CurrentSessionLogPath));
        }
    };
}
```
