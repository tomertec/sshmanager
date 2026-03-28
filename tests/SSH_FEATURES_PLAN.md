# SSH Features Implementation Plan

## Overview

Implementation plan for 5 advanced SSH features in the SshManager WPF application.

---

## Feature 1: GSSAPI/Kerberos Authentication

**Goal**: Enterprise SSO support using Windows domain credentials.

### Data Model Changes

**`src/SshManager.Core/Models/AuthType.cs`** - Add enum value:
```csharp
Kerberos = 3  // GSSAPI/Kerberos authentication
```

**`src/SshManager.Core/Models/HostEntry.cs`** - Add properties:
- `KerberosServicePrincipal` (string?) - e.g., "host/server.domain.com"
- `KerberosDelegateCredentials` (bool) - Enable ticket forwarding

**`src/SshManager.Core/Models/AppSettings.cs`** - Add:
- `EnableKerberosAuth` (bool)
- `DefaultKerberosDelegation` (bool)

### New Services

**`src/SshManager.Terminal/Services/IKerberosAuthService.cs`**:
- `GetStatusAsync()` - Check TGT availability, realm, principal
- `HasValidTicketAsync(servicePrincipal)` - Verify ticket exists
- Uses Windows SSPI via `System.Security.Principal.Windows`

### Modifications

**`src/SshManager.Terminal/Services/SshAuthenticationFactory.cs`**:
- Add `case AuthType.Kerberos:` in `CreateAuthMethods()`
- Create `GssApiWithMicAuthenticationMethod` (SSH.NET built-in)

### UI Changes

**`src/SshManager.App/Views/Dialogs/HostEditDialog.xaml`**:
- Add "Kerberos" to AuthType ComboBox
- Show Kerberos settings panel when selected
- Kerberos status indicator with TGT info

---

## Feature 2: Connection Pooling

**Goal**: Reuse SSH connections for multiple sessions to same host.

### Data Model Changes

**`src/SshManager.Core/Models/AppSettings.cs`** - Add:
- `EnableConnectionPooling` (bool, default: false)
- `ConnectionPoolMaxPerHost` (int, default: 3)
- `ConnectionPoolIdleTimeoutSeconds` (int, default: 300)

### New Services

**`src/SshManager.Terminal/Services/IConnectionPool.cs`**:
```csharp
public interface IConnectionPool
{
    Task<IPooledConnection> AcquireAsync(ConnectionPoolKey key, Func<Task<SshClient>> factory, CancellationToken ct);
    void Release(IPooledConnection connection);
    PoolStats GetStats();
    Task DrainAsync(CancellationToken ct);
}

public record ConnectionPoolKey(string Hostname, int Port, string Username, AuthType AuthType, string? PrivateKeyPath);
```

**`src/SshManager.Terminal/Services/ConnectionPool.cs`**:
- Thread-safe ConcurrentDictionary keyed by host identity
- Reference counting for active shell streams
- Idle connection cleanup timer
- Max connections semaphore per host

**`src/SshManager.Terminal/Services/PooledSshConnection.cs`**:
- Wrapper returning connection to pool on dispose
- Creates new ShellStream per terminal session
- Tracks shell stream count

### Modifications

**`src/SshManager.Terminal/Services/SshConnectionService.cs`**:
- Inject `IConnectionPool`
- Check pool before creating new connection
- Return pooled connection if available

### UI Changes

**`src/SshManager.App/Views/Dialogs/SettingsDialog.xaml`**:
- Connection Pooling section with enable toggle
- Max connections and idle timeout sliders

---

## Feature 3: X11 Forwarding

**Goal**: Display Linux GUI applications on Windows via X server.

### Data Model Changes

**`src/SshManager.Core/Models/HostEntry.cs`** - Add:
- `X11ForwardingEnabled` (bool?, null = use global)
- `X11TrustedForwarding` (bool) - `-Y` vs `-X`
- `X11DisplayNumber` (int?, default: 0)

**`src/SshManager.Core/Models/AppSettings.cs`** - Add:
- `DefaultX11ForwardingEnabled` (bool)
- `X11ServerPath` (string) - Path to VcXsrv/Xming
- `AutoLaunchXServer` (bool)

### New Services

**`src/SshManager.Terminal/Services/IX11ForwardingService.cs`**:
```csharp
public interface IX11ForwardingService
{
    Task<X11ServerStatus> DetectXServerAsync(CancellationToken ct);
    Task<bool> LaunchXServerAsync(string path, CancellationToken ct);
    Task SetupForwardingAsync(ISshConnection connection, X11ForwardingSettings settings, CancellationToken ct);
}

public record X11ServerStatus(bool IsAvailable, string DisplayAddress, int DisplayNumber, string? ServerName);
```

**`src/SshManager.Terminal/Services/X11ForwardingService.cs`**:
- Detect running X servers (check TCP 6000+display, named pipes)
- Launch VcXsrv/Xming/X410 if configured
- Set DISPLAY environment variable on remote
- Generate/manage MIT-MAGIC-COOKIE for X11 auth

### Modifications

**`src/SshManager.Terminal/Services/SshConnectionService.cs`**:
- Call `IX11ForwardingService.SetupForwardingAsync()` after shell creation
- Pass X11 settings from `TerminalConnectionInfo`

### UI Changes

**`src/SshManager.App/Views/Dialogs/HostEditDialog.xaml`**:
- X11 Forwarding section in Advanced Options (SSH only)
- Enable toggle, trusted/untrusted option
- Display number selector

**`src/SshManager.App/Views/Dialogs/SettingsDialog.xaml`**:
- X11 Server configuration section
- X server path browser, auto-launch toggle
- Detect button with status display

---

## Feature 4: SSH Tunnel Visual Builder

**Goal**: Visual drag-and-drop interface for complex tunnel configurations.

### Data Model Changes

**New file: `src/SshManager.Core/Models/TunnelProfile.cs`**:
```csharp
public sealed class TunnelProfile
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
    public ICollection<TunnelNode> Nodes { get; set; }
    public ICollection<TunnelEdge> Edges { get; set; }
}

public sealed class TunnelNode
{
    public Guid Id { get; set; }
    public TunnelNodeType NodeType { get; set; }  // LocalMachine, SshHost, PortForward, SocksProxy
    public Guid? HostId { get; set; }  // Reference to HostEntry
    public string Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int? LocalPort { get; set; }
    public int? RemotePort { get; set; }
    public string? RemoteHost { get; set; }
}

public sealed class TunnelEdge
{
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
}
```

### New Services

**`src/SshManager.Terminal/Services/ITunnelBuilderService.cs`**:
- `Validate(TunnelProfile)` - Check graph is valid
- `GenerateSshCommand(TunnelProfile)` - Generate equivalent SSH command
- `ExecuteAsync(TunnelProfile)` - Build and execute tunnel chain

### New UI Components

**`src/SshManager.App/Views/Dialogs/TunnelBuilderDialog.xaml`**:
- Canvas-based visual editor with zoom/pan
- Node palette (drag sources): Local, SSH Host, Port Forward, SOCKS
- Connection lines with arrows between nodes
- Property panel for selected node
- Toolbar: Save, Load, Execute, Copy SSH Command
- Command preview pane

**`src/SshManager.App/Views/Controls/TunnelCanvas.xaml`**:
- WPF Canvas with ItemsControl for nodes
- Bezier curve paths for connections
- Mouse handlers for drag-drop positioning

**`src/SshManager.App/ViewModels/TunnelBuilderViewModel.cs`**:
- ObservableCollection<TunnelNodeViewModel>
- ObservableCollection<TunnelEdgeViewModel>
- Commands: AddNode, RemoveNode, Connect, Execute

---

## Feature 5: Autocompletion (Tab Completion)

**Goal**: Tab completion for remote commands and file paths.

### Data Model Changes

**`src/SshManager.Core/Models/AppSettings.cs`** - Add:
- `EnableAutocompletion` (bool, default: false)
- `AutocompletionMode` (enum: RemoteShell, LocalHistory, Hybrid)
- `AutocompletionDebounceMs` (int, default: 150)

**New file: `src/SshManager.Core/Models/CommandHistoryEntry.cs`**:
```csharp
public sealed class CommandHistoryEntry
{
    public Guid Id { get; set; }
    public Guid? HostId { get; set; }
    public string Command { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
    public int UseCount { get; set; }
}
```

### New Services

**`src/SshManager.Terminal/Services/IAutocompletionService.cs`**:
```csharp
public interface IAutocompletionService
{
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        ISshConnection connection,
        string currentLine,
        int cursorPosition,
        CancellationToken ct);
}

public record CompletionItem(string Text, string DisplayText, CompletionItemType Type, int Score);
public enum CompletionItemType { Command, FilePath, Directory, Argument, History }
```

**`src/SshManager.Terminal/Services/AutocompletionService.cs`**:
- RemoteShell mode: Execute `compgen -c prefix` for commands, `compgen -f prefix` for files
- LocalHistory mode: Query CommandHistoryEntry by prefix
- Hybrid: Combine and rank by relevance
- Cache common completions per host

**`src/SshManager.Data/Repositories/ICommandHistoryRepository.cs`**:
- `AddAsync(hostId, command)`
- `GetSuggestionsAsync(hostId, prefix, maxResults)`

### New UI Components

**`src/SshManager.App/Views/Controls/CompletionPopup.xaml`**:
- Lightweight popup near cursor position
- ListView with completion items and icons
- Keyboard navigation: Up/Down/Enter/Escape/Tab

### Modifications

**`src/SshManager.Terminal/Services/TerminalKeyboardHandler.cs`**:
- Add Tab key interception (line ~44):
```csharp
if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
{
    context.RequestCompletions();
    return true;
}
```

**`src/SshManager.Terminal/Services/IKeyboardHandlerContext.cs`**:
- Add `RequestCompletions()` method
- Add `InsertCompletion(string text)` method
- Add `string GetCurrentInputLine()` property

**`src/SshManager.Terminal/Controls/SshTerminalControl.xaml.cs`**:
- Track current input line (since last newline/Enter)
- Host CompletionPopup control
- Handle completion selection

---

## Implementation Order

| Order | Feature | Complexity | Notes |
|-------|---------|------------|-------|
| 1 | GSSAPI/Kerberos | Medium | SSH.NET has built-in support |
| 2 | Connection Pooling | Medium | Pure .NET, no dependencies |
| 3 | Autocompletion | High | Requires input tracking + popup |
| 4 | X11 Forwarding | High | External X server dependency |
| 5 | Tunnel Builder | Very High | Visual editor is complex |

---

## Key Files to Modify

| File | Features |
|------|----------|
| `src/SshManager.Core/Models/AuthType.cs` | Kerberos |
| `src/SshManager.Core/Models/HostEntry.cs` | Kerberos, X11 |
| `src/SshManager.Core/Models/AppSettings.cs` | All features |
| `src/SshManager.Terminal/Services/SshAuthenticationFactory.cs` | Kerberos |
| `src/SshManager.Terminal/Services/SshConnectionService.cs` | Pool, X11 |
| `src/SshManager.Terminal/Services/TerminalKeyboardHandler.cs` | Autocompletion |
| `src/SshManager.App/Views/Dialogs/HostEditDialog.xaml` | Kerberos, X11 |
| `src/SshManager.App/Views/Dialogs/SettingsDialog.xaml` | All features |
| `src/SshManager.Data/AppDbContext.cs` | CommandHistory, TunnelProfile |

---

## Verification Plan

1. **Kerberos**: Test on domain-joined machine with Active Directory
   - Verify TGT detection shows correct realm/principal
   - Connect to SSH server with GSSAPI enabled

2. **Connection Pooling**: Open multiple tabs to same host
   - Verify only one SshClient created (check logs)
   - Test idle timeout cleanup

3. **X11 Forwarding**: Install VcXsrv, connect to Linux
   - Run `xeyes` or `xclock` on remote
   - Verify window appears on Windows

4. **Tunnel Builder**: Create multi-hop tunnel visually
   - Verify generated SSH command is correct
   - Execute and confirm connectivity

5. **Autocompletion**: Type partial command, press Tab
   - Verify popup appears with suggestions
   - Select and confirm insertion works
