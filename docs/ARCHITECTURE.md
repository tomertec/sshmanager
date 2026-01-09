# Architecture Overview

This document explains the high-level architecture of SshManager, including how different components interact, key design decisions, and where to make common changes.

**Target audience:** Developers who need to understand the system design before making significant changes.

## System Design

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        SshManager.App                            │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │   Views     │  │  ViewModels │  │      Services           │  │
│  │   (XAML)    │──│   (MVVM)    │──│  (App-level logic)      │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
└──────────────────────────────┬──────────────────────────────────┘
                               │
        ┌──────────────────────┼──────────────────────┐
        ▼                      ▼                      ▼
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│ SshManager    │    │ SshManager    │    │ SshManager    │
│   .Terminal   │    │   .Security   │    │   .Data       │
│               │    │               │    │               │
│ ┌───────────┐ │    │ ┌───────────┐ │    │ ┌───────────┐ │
│ │ Controls  │ │    │ │ DPAPI     │ │    │ │ EF Core   │ │
│ │ Services  │ │    │ │ KeyMgmt   │ │    │ │ SQLite    │ │
│ │ SSH.NET   │ │    │ │ CredCache │ │    │ │ Repos     │ │
│ └───────────┘ │    │ └───────────┘ │    │ └───────────┘ │
└───────────────┘    └───────────────┘    └───────────────┘
        │                    │                    │
        └────────────────────┼────────────────────┘
                             ▼
                    ┌───────────────┐
                    │ SshManager    │
                    │   .Core       │
                    │               │
                    │ ┌───────────┐ │
                    │ │ Models    │ │
                    │ │ Enums     │ │
                    │ └───────────┘ │
                    └───────────────┘
```

### Layer Responsibilities

| Layer | Project | Responsibility |
|-------|---------|---------------|
| **Presentation** | SshManager.App | UI, ViewModels, user interaction |
| **Terminal** | SshManager.Terminal | SSH connections, terminal rendering |
| **Security** | SshManager.Security | Encryption, key management, credentials |
| **Data** | SshManager.Data | Database access, repositories |
| **Domain** | SshManager.Core | Models, enums, shared types |

### Technology Stack

| Component | Technology | Why We Chose It |
|-----------|-----------|-----------------|
| Framework | .NET 8 + WPF | Native Windows UI, mature ecosystem |
| UI Controls | WPF-UI 4.1.0 | Modern Fluent Design for WPF |
| MVVM | CommunityToolkit.Mvvm | Source generators, less boilerplate |
| Database | SQLite + EF Core | Embedded, zero-config, portable |
| SSH | SSH.NET | Mature, well-documented .NET SSH library |
| Terminal | xterm.js + WebView2 | Full VT100/ANSI support, GPU-accelerated |
| Logging | Serilog | Structured logging, multiple sinks |

## Directory Structure

```
sshmanager/
├── src/
│   ├── SshManager.Core/                 # Domain layer (no dependencies)
│   │   └── Models/
│   │       ├── HostEntry.cs            # SSH host configuration
│   │       ├── HostGroup.cs            # Host grouping
│   │       ├── AuthType.cs             # Authentication enum
│   │       ├── AppSettings.cs          # Application settings model
│   │       ├── ConnectionHistory.cs    # Connection history entries
│   │       ├── CommandSnippet.cs       # Saved command snippets
│   │       ├── HostFingerprint.cs      # SSH host key fingerprints
│   │       ├── ManagedSshKey.cs        # Managed SSH key metadata
│   │       ├── ProxyJumpProfile.cs     # Jump host configurations
│   │       └── PortForwardingProfile.cs # Port forwarding configs
│   │
│   ├── SshManager.Data/                 # Data access layer
│   │   ├── AppDbContext.cs             # EF Core DbContext
│   │   ├── DbPaths.cs                  # Database file location helper
│   │   └── Repositories/
│   │       ├── IHostRepository.cs      # Host CRUD interface
│   │       ├── HostRepository.cs       # Host CRUD implementation
│   │       ├── IGroupRepository.cs     # Group CRUD interface
│   │       ├── GroupRepository.cs      # Group CRUD implementation
│   │       ├── ISettingsRepository.cs  # Settings interface
│   │       ├── SettingsRepository.cs   # Settings implementation
│   │       └── ...                     # Other repositories
│   │
│   ├── SshManager.Security/             # Security layer
│   │   ├── ISecretProtector.cs         # Encryption interface
│   │   ├── DpapiSecretProtector.cs     # DPAPI implementation
│   │   ├── ICredentialCache.cs         # Credential cache interface
│   │   ├── SecureCredentialCache.cs    # Secure in-memory cache
│   │   ├── ISshKeyManager.cs           # SSH key management interface
│   │   └── SshKeyManagerService.cs     # SSH key operations
│   │
│   ├── SshManager.Terminal/             # Terminal layer
│   │   ├── Controls/
│   │   │   ├── SshTerminalControl.xaml # Main terminal control
│   │   │   ├── WebTerminalControl.xaml # WebView2 xterm.js wrapper
│   │   │   ├── TerminalFindOverlay.xaml # Search overlay
│   │   │   └── TerminalStatusBar.xaml  # Status bar control
│   │   │
│   │   ├── Services/
│   │   │   ├── ISshConnectionService.cs # SSH connection interface
│   │   │   ├── SshConnectionService.cs  # SSH.NET connection logic
│   │   │   ├── SshTerminalBridge.cs     # SSH <-> Terminal bridge
│   │   │   ├── WebTerminalBridge.cs     # C# <-> JavaScript bridge
│   │   │   ├── ITerminalSessionManager.cs # Session management
│   │   │   ├── TerminalSessionManager.cs  # Active sessions
│   │   │   ├── IProxyJumpService.cs     # Jump host tunneling
│   │   │   ├── IPortForwardingService.cs # Port forwarding
│   │   │   └── ISftpService.cs          # SFTP operations
│   │   │
│   │   └── Resources/
│   │       └── Terminal/
│   │           └── terminal.html        # Embedded xterm.js terminal
│   │
│   └── SshManager.App/                  # Application layer
│       ├── App.xaml                     # Application resources
│       ├── App.xaml.cs                  # DI setup, startup logic
│       │
│       ├── Views/
│       │   ├── Windows/
│       │   │   ├── MainWindow.xaml      # Main application window
│       │   │   ├── SftpBrowserWindow.xaml # SFTP file browser
│       │   │   └── TextEditorWindow.xaml  # Remote file editor
│       │   │
│       │   ├── Dialogs/
│       │   │   ├── HostEditDialog.xaml  # Add/edit host
│       │   │   ├── GroupDialog.xaml     # Add/edit group
│       │   │   ├── SettingsDialog.xaml  # Application settings
│       │   │   └── ...                  # Other dialogs
│       │   │
│       │   └── Controls/
│       │       ├── TerminalPane.xaml    # Terminal tab content
│       │       ├── TerminalPaneContainer.xaml # Split pane container
│       │       └── ...                  # Other controls
│       │
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs   # Main window logic
│       │   ├── HostEditViewModel.cs     # Host editing logic
│       │   └── ...                      # Other view models
│       │
│       ├── Services/
│       │   ├── IExportImportService.cs  # Import/export interface
│       │   ├── ExportImportService.cs   # Import/export logic
│       │   ├── ISshConfigParser.cs      # SSH config parser interface
│       │   ├── SshConfigParser.cs       # Parse ~/.ssh/config
│       │   ├── IBackupService.cs        # Backup interface
│       │   ├── BackupService.cs         # Database backup
│       │   ├── ICloudSyncService.cs     # Cloud sync interface
│       │   └── CloudSyncService.cs      # OneDrive sync
│       │
│       └── Converters/                  # XAML value converters
│
├── tests/
│   └── SshManager.Terminal.Tests/       # Terminal unit tests
│
└── docs/                                # Documentation
```

### Directory Purpose and Rules

#### SshManager.Core
**Purpose:** Contains domain models and shared types. Has no external dependencies.

**What goes here:**
- Entity models (HostEntry, HostGroup, etc.)
- Enumerations (AuthType, etc.)
- Shared constants and interfaces

**What doesn't go here:**
- Business logic (put in appropriate layer)
- UI-related code (put in App)
- Database access code (put in Data)

**When to add a file:** When you need a new domain model or shared type.

#### SshManager.Data
**Purpose:** Data access layer using Entity Framework Core with SQLite.

**What goes here:**
- DbContext and entity configurations
- Repository implementations
- Database path helpers

**What doesn't go here:**
- Business logic beyond CRUD operations
- UI code
- SSH or terminal code

**When to add a file:** When you need a new repository or data access pattern.

#### SshManager.Security
**Purpose:** Security-related code including encryption and key management.

**What goes here:**
- Encryption/decryption implementations
- Credential caching
- SSH key management
- Security-related interfaces

**What doesn't go here:**
- SSH connection logic (put in Terminal)
- UI code
- Database access

**When to add a file:** When you need new security functionality.

#### SshManager.Terminal
**Purpose:** SSH connections and terminal rendering.

**What goes here:**
- SSH connection services (using SSH.NET)
- Terminal WPF controls
- Terminal bridges (SSH <-> UI)
- SFTP and port forwarding services

**What doesn't go here:**
- UI dialogs and windows (put in App)
- Data persistence (put in Data)
- Non-terminal UI controls

**When to add a file:** When you need new SSH or terminal functionality.

#### SshManager.App
**Purpose:** WPF application with UI and orchestration.

**What goes here:**
- Views (Windows, Dialogs, Controls)
- ViewModels
- Application services (import/export, backup, etc.)
- DI configuration
- Application lifecycle management

**What doesn't go here:**
- Low-level SSH code (put in Terminal)
- Database access (put in Data)
- Encryption code (put in Security)

**When to add a file:** When you need new UI or application-level services.

## Data Flow

### SSH Connection Flow

```
User Double-clicks Host
        │
        ▼
MainWindowViewModel.ConnectToHostAsync()
        │
        ▼
TerminalSessionManager.CreateSessionAsync()
        │
        ▼
SshConnectionService.ConnectAsync()
        │
        ├── Password Auth: ISecretProtector.Unprotect()
        ├── PrivateKey Auth: Load key file
        └── SshAgent Auth: Use OpenSSH agent
        │
        ▼
SSH.NET SshClient.Connect()
        │
        ▼
SshClient.CreateShellStream()
        │
        ▼
SshTerminalBridge ←→ WebTerminalBridge ←→ xterm.js
```

**Step-by-step:**

1. **User triggers connection** in MainWindow
   - Double-click, right-click Connect, or press Enter

2. **ViewModel initiates connection** in MainWindowViewModel
   - Calls `TerminalSessionManager.CreateSessionAsync(hostEntry)`
   - Creates new tab in terminal pane

3. **SSH connection established** in SshConnectionService
   - Decrypts password if needed via `ISecretProtector`
   - Creates SSH.NET `SshClient` with appropriate authentication
   - Opens `ShellStream` for terminal I/O

4. **Terminal bridges created**
   - `SshTerminalBridge`: Reads from ShellStream, writes to WebTerminalBridge
   - `WebTerminalBridge`: Sends data to xterm.js via WebView2 messaging

5. **Terminal renders output**
   - xterm.js in WebView2 processes ANSI escape sequences
   - User input sent back through the bridge chain

### Terminal Data Flow (Detail)

```
┌─────────────────┐
│   SSH Server    │
└────────┬────────┘
         │ TCP/SSH Protocol
         ▼
┌─────────────────┐
│  SSH.NET        │
│  ShellStream    │
└────────┬────────┘
         │ byte[] data
         ▼
┌─────────────────┐    Reads data, buffers if terminal not ready
│ SshTerminalBridge│
└────────┬────────┘
         │ string data
         ▼
┌─────────────────┐    PostWebMessageAsJson() to WebView2
│ WebTerminalBridge│
└────────┬────────┘
         │ JSON: { type: "write", data: "..." }
         ▼
┌─────────────────┐
│   WebView2      │
│  (xterm.js)     │──── Renders terminal, handles ANSI escapes
└────────┬────────┘
         │ JSON: { type: "input", data: "..." }
         ▼
┌─────────────────┐    WebMessageReceived event
│ WebTerminalBridge│
└────────┬────────┘
         │ string data
         ▼
┌─────────────────┐
│ SshTerminalBridge│──── Writes to ShellStream
└─────────────────┘
```

### Dependency Injection Setup

All services are registered in `App.xaml.cs`:

```csharp
// Database (scoped via factory)
services.AddDbContextFactory<AppDbContext>();

// Repositories (singletons using DbContextFactory)
services.AddSingleton<IHostRepository, HostRepository>();
services.AddSingleton<IGroupRepository, GroupRepository>();

// Security (singletons)
services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
services.AddSingleton<ICredentialCache, SecureCredentialCache>();

// Terminal services (singletons)
services.AddSingleton<ISshConnectionService, SshConnectionService>();
services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();

// ViewModels (singletons for main, transient for dialogs)
services.AddSingleton<MainWindowViewModel>();
services.AddTransient<HostEditViewModel>();
```

## Key Design Decisions

### Decision 1: DbContextFactory Pattern

**What we decided:** Use `IDbContextFactory<AppDbContext>` instead of direct DbContext injection.

**Context:**
- Repositories need to be singletons for performance
- DbContext is not thread-safe
- Multiple async operations may run concurrently

**Why we decided this:**
- Allows singleton repositories with properly scoped DbContexts
- Each operation gets its own DbContext via `CreateDbContextAsync()`
- DbContext is disposed after each operation via `await using`

**Trade-offs:**
- Pro: Thread-safe data access with singleton repositories
- Pro: No DbContext lifetime management issues
- Con: Slightly more verbose code
- Con: No change tracking across operations

**Code pattern:**
```csharp
public async Task<HostEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
{
    await using var db = await _dbFactory.CreateDbContextAsync(ct);
    return await db.Hosts.FindAsync([id], ct);
}
```

### Decision 2: WebView2 + xterm.js for Terminal

**What we decided:** Use WebView2 with xterm.js instead of a native WPF terminal control.

**Context:**
- Need full VT100/ANSI escape sequence support
- Need to render complex TUI applications (vim, htop, tmux)
- Native WPF terminal controls have limited escape sequence support

**Why we decided this:**
- xterm.js is battle-tested (used by VS Code, Theia, etc.)
- GPU-accelerated rendering via WebView2
- Full escape sequence support including 24-bit color
- Active community and maintenance

**Trade-offs:**
- Pro: Excellent terminal emulation quality
- Pro: Easy to customize themes and behavior
- Con: Requires WebView2 runtime (usually pre-installed)
- Con: JavaScript/C# bridge adds complexity

### Decision 3: DPAPI for Password Encryption

**What we decided:** Use Windows DPAPI for encrypting stored passwords.

**Context:**
- Need to securely store SSH passwords
- Passwords should be protected at rest
- Solution should be Windows-specific (acceptable for WPF app)

**Why we decided this:**
- DPAPI is built into Windows, no external dependencies
- Encryption is tied to the Windows user account
- Key management handled by Windows

**Trade-offs:**
- Pro: Strong encryption without key management complexity
- Pro: Passwords can't be decrypted by other Windows users
- Con: Passwords can't be transferred to other machines
- Con: Windows-only solution

### Decision 4: MVVM with CommunityToolkit.Mvvm

**What we decided:** Use CommunityToolkit.Mvvm with source generators.

**Context:**
- WPF applications benefit from MVVM pattern
- Need to minimize boilerplate code
- Want compile-time safety

**Why we decided this:**
- Source generators create INotifyPropertyChanged implementation
- `[ObservableProperty]` attribute reduces property boilerplate
- `[RelayCommand]` attribute simplifies command implementation
- Full integration with Visual Studio and analyzers

**Code pattern:**
```csharp
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private HostEntry? _selectedHost;

    [RelayCommand]
    private async Task ConnectAsync(HostEntry host)
    {
        // Command implementation
    }
}
```

## Module Dependencies

### Dependency Graph

```
SshManager.App
    ├─→ SshManager.Terminal
    │       └─→ SshManager.Core
    ├─→ SshManager.Security
    │       └─→ SshManager.Core
    ├─→ SshManager.Data
    │       └─→ SshManager.Core
    └─→ SshManager.Core

Dependencies flow downward only.
Core has no dependencies on other projects.
```

### Dependency Rules

1. **Core has no project dependencies**
   - Only models and shared types
   - Can reference .NET base libraries only

2. **Data, Security, Terminal depend only on Core**
   - These layers are independent of each other
   - Can be tested in isolation

3. **App depends on all projects**
   - Orchestrates all layers
   - Contains DI configuration

4. **No circular dependencies**
   - Dependencies always flow downward

### External Dependencies

| Package | Version | Used In | Purpose |
|---------|---------|---------|---------|
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.0 | Data | Database access |
| SSH.NET | 2024.2.0 | Terminal | SSH connections |
| Microsoft.Web.WebView2 | 1.0.2739.15 | Terminal | Terminal rendering |
| WPF-UI | 4.1.0 | App | UI controls |
| CommunityToolkit.Mvvm | 8.4.0 | App | MVVM framework |
| Serilog | Various | App | Logging |
| H.NotifyIcon.Wpf | 2.1.3 | App | System tray |
| AvalonEdit | 6.3.0 | App | Text editor |

## Extension Points

### Adding a New Host Property

1. **Add property to model** in `SshManager.Core/Models/HostEntry.cs`
2. **Add database column** migration in `App.xaml.cs:ApplySchemaMigrationsAsync()`
3. **Update UI** in `SshManager.App/Views/Dialogs/HostEditDialog.xaml`
4. **Update ViewModel** in `SshManager.App/ViewModels/HostEditViewModel.cs`

### Adding a New Authentication Type

1. **Add enum value** to `SshManager.Core/Models/AuthType.cs`
2. **Handle in connection service** `SshManager.Terminal/Services/SshConnectionService.cs`
3. **Update host edit UI** to configure new auth type
4. **Add security handling** if credentials need encryption

### Adding a New Terminal Feature

1. **JavaScript side**: Update `terminal.html` to add new command handler
2. **C# side**: Update `WebTerminalBridge.cs` to send new commands
3. **UI side**: Add controls or menu items to trigger the feature

### Adding a New Repository

1. **Create interface** in `SshManager.Data/Repositories/INewRepository.cs`
2. **Create implementation** in `SshManager.Data/Repositories/NewRepository.cs`
3. **Add DbSet** to `AppDbContext.cs` if new entity
4. **Register in DI** in `App.xaml.cs:ConfigureServices()`

## Security Architecture

### Password Encryption Flow

```
User enters password
        │
        ▼
ISecretProtector.Protect(password)
        │
        ▼
DpapiSecretProtector uses ProtectedData.Protect()
        │
        ▼
Base64-encoded encrypted bytes stored in PasswordProtected column
        │
        ▼
On connection, ISecretProtector.Unprotect() reverses the process
```

### Credential Caching

```
User connects with password/passphrase
        │
        ▼
ICredentialCache.Store(key, credential, timeout)
        │
        ▼
SecureCredentialCache stores in memory (encrypted)
        │
        ▼
Automatic expiration after timeout
        │
        ▼
Cleared on: Windows lock, app exit, manual clear
```

### Host Key Verification

1. First connection: User prompted to verify fingerprint
2. Fingerprint stored in `HostFingerprints` table
3. Subsequent connections: Fingerprint compared automatically
4. Mismatch triggers warning dialog

## Troubleshooting

### Common Architecture Issues

**Issue: DbContext disposed before async operation completes**
- **Symptoms:** `ObjectDisposedException` in repository methods
- **Cause:** Not using `await using` properly
- **Solution:** Always use `await using var db = await _dbFactory.CreateDbContextAsync(ct);`

**Issue: Terminal data loss on fast output**
- **Symptoms:** Missing characters in terminal output
- **Cause:** Data arriving before xterm.js is ready
- **Solution:** `WebTerminalBridge` buffers data until "ready" message received

**Issue: UI freeze during SSH operations**
- **Symptoms:** Window becomes unresponsive during connection
- **Cause:** Blocking calls on UI thread
- **Solution:** All SSH operations must be async, use `Task.Run()` for CPU-bound work

**Issue: Memory leak with many terminal tabs**
- **Symptoms:** Memory grows with each new tab
- **Cause:** WebView2 or bridges not properly disposed
- **Solution:** Ensure `SshTerminalBridge.Dispose()` called when tab closes
