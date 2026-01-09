# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SshManager is a Windows WPF application for managing SSH connections. It provides a modern UI for storing SSH host configurations and launching SSH sessions with an embedded terminal. Passwords are encrypted using Windows DPAPI (Data Protection API).

## Common Commands

### Build & Run
```bash
# Build the entire solution
dotnet build SshManager.sln

# Run the application
dotnet run --project src/SshManager.App/SshManager.App.csproj

# Build in Release mode
dotnet build SshManager.sln -c Release
```

### Database
- Database location: `%LocalAppData%\SshManager\sshmanager.db`
- The database is SQLite and created automatically on first run via `db.Database.EnsureCreatedAsync()` in App.xaml.cs

## Architecture

### Project Structure

The solution follows a layered architecture with 5 projects:

1. **SshManager.Core** - Domain models and shared types
   - No external dependencies
   - Contains `HostEntry`, `HostGroup`, `ConnectionHistory`, `AppSettings` models
   - Contains `AuthType` enum

2. **SshManager.Data** - Data access layer using EF Core + SQLite
   - Depends on: `SshManager.Core`
   - Uses DbContextFactory pattern for async operations
   - Repositories: `HostRepository`, `GroupRepository`, `ConnectionHistoryRepository`, `SettingsRepository`

3. **SshManager.Security** - Password encryption using DPAPI
   - Depends on: `SshManager.Core`
   - `DpapiSecretProtector`: encrypts/decrypts passwords for local user
   - Uses entropy string "SshManager::v1::2024" for additional protection

4. **SshManager.Terminal** - SSH connection and terminal control
   - Depends on: `SshManager.Core`
   - Uses SSH.NET for SSH connections
   - Uses WebView2 + xterm.js for terminal rendering
   - `SshConnectionService`: establishes SSH connections with all auth types
   - `TerminalSessionManager`: manages active terminal sessions
   - `SshTerminalControl`: Main WPF control that orchestrates terminal sessions
   - `WebTerminalControl`: WPF control wrapping WebView2 with xterm.js
   - `SshTerminalBridge`: bridges SSH.NET ShellStream with terminal I/O
   - `WebTerminalBridge`: bridges C# with JavaScript (xterm.js) via WebView2 messaging

5. **SshManager.App** - WPF UI using WPF-UI library
   - Depends on: All other projects
   - Uses MVVM pattern with CommunityToolkit.Mvvm
   - Dependency injection via Microsoft.Extensions.Hosting
   - Dark theme with Fluent Design

### Dependency Injection Setup

The app uses Generic Host with DI configured in App.xaml.cs. Key registrations:
- EF Core: `AddDbContextFactory<AppDbContext>` with SQLite
- Services: All registered as singletons (repositories, SSH services, security)
- ViewModels: `MainWindowViewModel` as singleton
- Windows: `MainWindow` as singleton

### Authentication Flow

Three authentication types in `AuthType` enum:
1. **SshAgent** (default) - Uses SSH keys from ~/.ssh/ directory
2. **PrivateKeyFile** - Uses specified private key file path
3. **Password** - Stores DPAPI-encrypted password in `HostEntry.PasswordProtected` as base64

### Data Access Pattern

Uses `IDbContextFactory<AppDbContext>` pattern:
```csharp
await using var db = await _dbFactory.CreateDbContextAsync(ct);
// Use db
// Disposed automatically via await using
```

This pattern allows singleton repositories with properly scoped DbContexts.

### UI Framework

- **WPF-UI library** (v4.1.0): Modern Fluent Design controls
- Dark theme set in App.xaml via `ui:ThemesDictionary`
- Split view layout: host list on left, terminal tabs on right
- MVVM with CommunityToolkit.Mvvm attributes (`[ObservableProperty]`, `[RelayCommand]`)

### Terminal Implementation

The terminal uses SSH.NET for connections and WebView2 + xterm.js for rendering:

**Key Components:**
- `SshConnectionService`: Creates SSH connections with ShellStream
- `SshTerminalControl`: Main WPF control that orchestrates terminal sessions
- `WebTerminalControl`: WPF UserControl wrapping WebView2 with embedded xterm.js terminal
- `WebTerminalBridge`: Handles C# ↔ JavaScript communication via `PostWebMessageAsJson`/`WebMessageReceived`
- `SshTerminalBridge`: Bridges SSH.NET ShellStream with terminal control
- `ThemeAdapter`: Converts themes to xterm.js format via `ToXtermTheme()`
- `TerminalOutputBuffer`: Text-based storage for search functionality
- `TerminalTextSearchService`: Text-based search through terminal output

**Data Flow:**
```
SSH.NET ShellStream ←→ SshTerminalBridge ←→ WebTerminalBridge ←→ WebView2 ←→ xterm.js
```

**Terminal Web Resources (Embedded):**
- `Resources/Terminal/terminal.html` - Self-contained HTML with inlined CSS and JavaScript
- Uses xterm.js v5.5.0 and xterm-addon-fit v0.10.0 from CDN
- JavaScript handles: write, resize, setTheme, focus, clear commands
- Sends to C#: input, ready, resized messages

**C# ↔ JavaScript Message Protocol:**
```javascript
// C# to JavaScript (via PostWebMessageAsJson):
{ type: "write", data: "text" }      // Write text to terminal
{ type: "resize", cols: 80, rows: 24 } // Resize terminal
{ type: "setTheme", theme: {...} }   // Apply color theme
{ type: "focus" }                     // Focus terminal
{ type: "clear" }                     // Clear terminal

// JavaScript to C# (via postMessage):
{ type: "input", data: "text" }      // User typed input
{ type: "ready" }                     // Terminal initialized
{ type: "resized", cols: X, rows: Y } // Terminal was resized
```

**Key Features:**
- WebView2-based rendering (Chromium engine)
- Full VT100/ANSI escape sequence support via xterm.js
- Proper docker compose, vim, tmux, htop rendering
- 24-bit color support
- Data buffering before terminal ready (prevents data loss)
- Tab switching preserves terminal state (uses Visibility.Hidden, not Collapsed)

### Important Implementation Notes

1. **DPAPI Security**: Passwords are encrypted per-user. They cannot be decrypted by other Windows users or on different machines.

2. **Terminal Sessions**: Managed by `TerminalSessionManager` with observable collection for UI binding.

3. **Database Seeding**: On first run, a sample host entry is created if the database is empty (App.xaml.cs).

4. **Connection History**: Tracks when connections are made via `IConnectionHistoryRepository.AddAsync()`.

5. **Tab Switching**: Uses `Visibility.Hidden` instead of `Collapsed` to keep WebView2 controls in the visual tree, preventing terminal state loss.

6. **Data Buffering**: `WebTerminalBridge` buffers SSH data arriving before xterm.js sends the "ready" message, then flushes when ready.

### Key Files to Understand

- `src/SshManager.App/App.xaml.cs` - DI container setup and app initialization
- `src/SshManager.App/ViewModels/MainWindowViewModel.cs` - Main business logic
- `src/SshManager.App/Views/Windows/MainWindow.xaml` - Main UI layout
- `src/SshManager.App/Views/Controls/TerminalPaneContainer.xaml.cs` - Pane management and tab switching
- `src/SshManager.Data/AppDbContext.cs` - EF Core model configuration
- `src/SshManager.Security/DpapiSecretProtector.cs` - Password encryption
- `src/SshManager.Terminal/Services/SshConnectionService.cs` - SSH connection logic
- `src/SshManager.Terminal/Controls/SshTerminalControl.xaml.cs` - Main terminal control
- `src/SshManager.Terminal/Controls/WebTerminalControl.xaml.cs` - WebView2 wrapper for xterm.js
- `src/SshManager.Terminal/Services/SshTerminalBridge.cs` - SSH ↔ C# data bridge
- `src/SshManager.Terminal/Services/WebTerminalBridge.cs` - C# ↔ JavaScript bridge
- `src/SshManager.Terminal/Services/ThemeAdapter.cs` - Theme conversion for xterm.js
- `src/SshManager.Terminal/Resources/Terminal/terminal.html` - Embedded xterm.js terminal

### NuGet Packages

- **Microsoft.EntityFrameworkCore.Sqlite** (9.0.0) - Database
- **SSH.NET** (2024.2.0) - SSH connections
- **Microsoft.Web.WebView2** (1.0.2739.15) - Terminal rendering via WebView2/xterm.js
- **WPF-UI** (4.1.0) - Modern UI controls
- **CommunityToolkit.Mvvm** (8.4.0) - MVVM framework
- **Microsoft.Extensions.Hosting** (9.0.0) - DI and hosting
- **System.Security.Cryptography.ProtectedData** (9.0.0) - DPAPI
