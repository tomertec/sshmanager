# SshManager

A modern Windows desktop application for managing SSH connections with an embedded terminal, built with WPF and .NET 8.

## What This Does

SshManager provides a graphical interface for storing and managing SSH host configurations, then connecting to them through a built-in terminal. Passwords are securely encrypted using Windows DPAPI, and the terminal supports full VT100/ANSI escape sequences including vim, tmux, docker, and htop.

## Quick Start

### Prerequisites

- Windows 10/11 (64-bit)
- .NET 8.0 SDK or later
- WebView2 Runtime (usually pre-installed on Windows 10/11)

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd sshmanager

# Build the solution
dotnet build SshManager.sln

# Run the application
dotnet run --project src/SshManager.App/SshManager.App.csproj
```

### Verify It's Working

1. The application should launch showing the main window with a split-view layout
2. A sample host entry "Sample Host" is created on first run
3. Double-click any host to open a terminal tab

## Features

### Core Features

- **Host Management**: Store SSH host configurations with hostname, port, username, and authentication settings
- **Group Organization**: Organize hosts into groups for better management
- **Embedded Terminal**: Full-featured xterm.js terminal with WebView2 rendering
- **Multiple Tabs**: Open multiple terminal sessions in tabs
- **Split Panes**: Split the terminal view horizontally or vertically

### Authentication

- **SSH Agent**: Use keys from your SSH agent or `~/.ssh/` directory
- **Private Key File**: Specify a path to a private key file
- **Password**: DPAPI-encrypted password storage (Windows user-specific)
- **Keyboard Interactive**: Support for 2FA and other interactive prompts

### Security

- **DPAPI Encryption**: Passwords are encrypted using Windows Data Protection API
- **Host Key Verification**: Verify and store SSH host fingerprints
- **Credential Caching**: Optional secure in-memory credential caching with configurable timeout
- **Session Lock Clearing**: Automatically clear cached credentials when Windows session locks

### Advanced Features

- **ProxyJump / Jump Hosts**: Configure multi-hop SSH connections through bastion hosts
- **Port Forwarding**: Local and remote port forwarding profiles
- **SFTP Browser**: Graphical file browser for remote systems
- **Remote File Editor**: Edit remote files with syntax highlighting
- **Command Snippets**: Save and reuse frequently used commands
- **Session Logging**: Log terminal sessions to files
- **Broadcast Input**: Send input to multiple terminals simultaneously
- **Connection History**: Track when and to which hosts you've connected

### Import/Export

- **SSH Config Import**: Import hosts from `~/.ssh/config`
- **PuTTY Import**: Import sessions from PuTTY registry
- **Backup/Restore**: Backup and restore all settings and hosts
- **Cloud Sync**: Sync hosts across devices via OneDrive (optional)

### UI Features

- **Dark Theme**: Modern Fluent Design with WPF-UI library
- **System Tray**: Minimize to system tray with quick-connect menu
- **Terminal Themes**: Customizable terminal color schemes
- **Host Status**: Monitor host availability with ping checks

## Project Structure

```
sshmanager/
├── src/
│   ├── SshManager.Core/           # Domain models and shared types
│   │   └── Models/                # HostEntry, HostGroup, AuthType, etc.
│   │
│   ├── SshManager.Data/           # Data access layer (EF Core + SQLite)
│   │   ├── AppDbContext.cs        # EF Core database context
│   │   └── Repositories/          # Repository implementations
│   │
│   ├── SshManager.Security/       # Password encryption and key management
│   │   ├── DpapiSecretProtector.cs    # DPAPI encryption
│   │   └── SecureCredentialCache.cs   # Secure memory credential caching
│   │
│   ├── SshManager.Terminal/       # SSH and terminal components
│   │   ├── Controls/              # WebTerminalControl, SshTerminalControl
│   │   ├── Services/              # SshConnectionService, SshTerminalBridge
│   │   └── Resources/             # terminal.html with xterm.js
│   │
│   └── SshManager.App/            # WPF UI application
│       ├── Views/                 # XAML views (Windows, Dialogs, Controls)
│       ├── ViewModels/            # MVVM view models
│       ├── Services/              # Application services
│       └── App.xaml.cs            # DI container and app initialization
│
├── tests/                         # Unit and integration tests
├── docs/                          # Documentation
└── SshManager.sln                 # Visual Studio solution file
```

## Key Concepts

### Authentication Types

SshManager supports three authentication methods defined in `AuthType` enum:

| Type | Description |
|------|-------------|
| **SshAgent** | Uses SSH keys from your `~/.ssh/` directory or SSH agent |
| **PrivateKeyFile** | Specify a custom private key file path |
| **Password** | Store encrypted password in the database |

### Data Storage

- **Database**: SQLite stored at `%LocalAppData%\SshManager\sshmanager.db`
- **Logs**: Rolling log files at `%LocalAppData%\SshManager\logs\`
- **Terminal Themes**: Custom themes stored in database

### Terminal Architecture

The terminal uses a bridge pattern to connect SSH.NET with xterm.js:

```
SSH Server  <-->  SSH.NET ShellStream  <-->  SshTerminalBridge
                                                    |
                                                    v
User Input  <-->  xterm.js (WebView2)  <-->  WebTerminalBridge
```

## Common Tasks

### Adding a New Host

1. Click the **+** button or right-click in the host list
2. Fill in the host details (hostname, port, username)
3. Select authentication type and configure credentials
4. Optionally assign to a group
5. Click **Save**

### Connecting to a Host

1. Double-click a host in the list, or
2. Right-click and select **Connect**, or
3. Select a host and press **Enter**

### Using Split Panes

1. Right-click on a terminal tab
2. Select **Split Horizontal** or **Split Vertical**
3. Drag and drop sessions between panes
4. Use keyboard shortcuts: `Ctrl+Shift+H` (horizontal) or `Ctrl+Shift+V` (vertical)

### Importing SSH Config

1. Go to **Settings** (gear icon) or **File > Import**
2. Select **Import from SSH Config**
3. Browse to your `~/.ssh/config` file
4. Select hosts to import
5. Click **Import**

## Configuration

### Application Settings

Access settings via the gear icon in the toolbar. Key settings include:

| Setting | Description |
|---------|-------------|
| **Terminal Theme** | Select terminal color scheme |
| **Scrollback Buffer** | Number of lines to keep in terminal history |
| **Session Logging** | Enable logging of terminal sessions |
| **Credential Caching** | Cache credentials in memory for repeated connections |
| **Auto Backup** | Automatically backup database at intervals |
| **Cloud Sync** | Sync hosts via OneDrive |

### Database Location

The SQLite database is stored at:
```
%LocalAppData%\SshManager\sshmanager.db
```

To backup manually, copy this file when the application is closed.

## Development

### Building

```bash
# Debug build
dotnet build SshManager.sln

# Release build
dotnet build SshManager.sln -c Release

# Run tests
dotnet test
```

### Publishing

```bash
# Self-contained Windows x64 executable
dotnet publish src/SshManager.App/SshManager.App.csproj -c Release -r win-x64 --self-contained
```

### Code Style

- MVVM pattern with CommunityToolkit.Mvvm
- Dependency injection via Microsoft.Extensions.Hosting
- Async/await for all I/O operations
- Repository pattern for data access

## Troubleshooting

### WebView2 Not Found

**Problem:** Application fails to start with WebView2 error

**Solution:** Install the WebView2 Runtime from Microsoft:
https://developer.microsoft.com/en-us/microsoft-edge/webview2/

### Connection Timeout

**Problem:** SSH connections time out immediately

**Solution:**
1. Verify the host is reachable: `ping hostname`
2. Check firewall settings allow outbound port 22
3. Verify SSH service is running on the target host

### Password Not Accepted

**Problem:** Saved password doesn't work

**Solution:**
1. DPAPI-encrypted passwords are user-specific. If you've changed Windows users, re-enter the password
2. Clear and re-enter the password in host settings
3. Check if the remote server requires a different authentication method

### Terminal Display Issues

**Problem:** Terminal shows garbled text or wrong colors

**Solution:**
1. Try a different terminal theme in Settings
2. Clear the terminal (right-click > Clear)
3. Check the remote system's `$TERM` environment variable

## Additional Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) - Detailed system architecture
- [GETTING_STARTED.md](./GETTING_STARTED.md) - Step-by-step beginner guide
- [CLAUDE.md](../CLAUDE.md) - Developer reference for AI assistants

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8, WPF |
| UI Library | WPF-UI (Fluent Design) |
| MVVM | CommunityToolkit.Mvvm |
| Database | SQLite via EF Core |
| SSH | SSH.NET |
| Terminal | xterm.js via WebView2 |
| Logging | Serilog |

## License

[License information to be added]
