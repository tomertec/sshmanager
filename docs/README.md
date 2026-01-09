# SshManager

A modern Windows desktop application for managing SSH and serial port connections with an embedded terminal, built with WPF and .NET 8.

## What This Does

SshManager provides a graphical interface for storing and managing SSH host configurations and serial port connections, then connecting to them through a built-in terminal. It supports SSH connections to remote servers as well as serial port (COM) connections to embedded devices, network equipment, routers, and switches. Passwords are securely encrypted using Windows DPAPI, and the terminal supports full VT100/ANSI escape sequences including vim, tmux, docker, and htop.

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
- **Tag System**: Apply colored tags to hosts for categorization and filtering
- **Environment Variables**: Configure per-host environment variables sent during SSH connection
- **Embedded Terminal**: Full-featured xterm.js terminal with WebView2 rendering
- **Multiple Tabs**: Open multiple terminal sessions in tabs
- **Split Panes**: Split the terminal view horizontally or vertically
- **Quick Connect**: Press `Ctrl+K` for command palette-style host search and connection

### Serial Port Connections

- **COM Port Support**: Connect to serial devices (routers, switches, Arduino, Raspberry Pi, network equipment)
- **Full Configuration**: Configure baud rate (300-230400), data bits (5-8), stop bits, parity, and flow control
- **Hardware Signal Control**: Toggle DTR (Data Terminal Ready) and RTS (Request To Send) signals
- **Send Break**: Send break signal to connected devices
- **Local Echo**: Optional local character echo for half-duplex devices
- **Line Endings**: Configurable line endings (CRLF, LF, CR)
- **Serial Quick Connect**: Dialog to enumerate available COM ports and connect instantly
- **Save Configurations**: Store serial port settings alongside SSH hosts for repeated use
- **Dual Driver Support**: Uses System.IO.Ports (primary) with RJCP.SerialPortStream fallback for maximum compatibility

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
- **Session Recording**: Record terminal sessions in ASCIINEMA v2 format for playback
- **Session Playback**: Replay recorded sessions with speed control and seeking
- **Session Logging**: Log terminal sessions to files
- **Broadcast Input**: Send input to multiple terminals simultaneously
- **Connection History**: Track when and to which hosts you've connected

### Import/Export

- **SSH Config Import**: Import hosts from `~/.ssh/config`
- **SSH Config Export**: Export hosts to OpenSSH config format with ProxyJump support
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
│   │   └── Models/                # HostEntry, HostGroup, AuthType, ConnectionType,
│   │                              # SerialPortSettings, etc.
│   │
│   ├── SshManager.Data/           # Data access layer (EF Core + SQLite)
│   │   ├── AppDbContext.cs        # EF Core database context
│   │   └── Repositories/          # Repository implementations
│   │
│   ├── SshManager.Security/       # Password encryption and key management
│   │   ├── DpapiSecretProtector.cs    # DPAPI encryption
│   │   └── SecureCredentialCache.cs   # Secure memory credential caching
│   │
│   ├── SshManager.Terminal/       # SSH, Serial, and terminal components
│   │   ├── Controls/              # WebTerminalControl, SshTerminalControl
│   │   ├── Models/                # SerialConnectionInfo
│   │   ├── Services/              # Connection and bridge services
│   │   │   ├── SshConnectionService.cs      # SSH connections
│   │   │   ├── SshTerminalBridge.cs         # SSH ↔ terminal bridge
│   │   │   ├── SerialConnectionService.cs   # Serial port connections
│   │   │   ├── SerialTerminalBridge.cs      # Serial ↔ terminal bridge
│   │   │   ├── Recording/         # Session recording (ASCIINEMA format)
│   │   │   └── Playback/          # Session playback with speed control
│   │   └── Resources/             # terminal.html with xterm.js
│   │
│   └── SshManager.App/            # WPF UI application
│       ├── Views/                 # XAML views (Windows, Dialogs, Controls)
│       │   └── Dialogs/           # Including SerialQuickConnectDialog
│       ├── ViewModels/            # MVVM view models
│       ├── Services/              # Application services
│       └── App.xaml.cs            # DI container and app initialization
│
├── docs/                          # Documentation
└── SshManager.sln                 # Visual Studio solution file
```

## Key Concepts

### Connection Types

SshManager supports two connection types defined in `ConnectionType` enum:

| Type | Description |
|------|-------------|
| **Ssh** | SSH connections to remote servers via SSH.NET |
| **Serial** | Serial port (COM) connections to local devices |

### Authentication Types (SSH)

SshManager supports three authentication methods defined in `AuthType` enum:

| Type | Description |
|------|-------------|
| **SshAgent** | Uses SSH keys from your `~/.ssh/` directory or SSH agent |
| **PrivateKeyFile** | Specify a custom private key file path |
| **Password** | Store encrypted password in the database |

### Serial Port Configuration

Serial connections support comprehensive hardware configuration:

| Setting | Options | Default |
|---------|---------|---------|
| **Baud Rate** | 300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400 | 9600 |
| **Data Bits** | 5, 6, 7, 8 | 8 |
| **Stop Bits** | None, One, OnePointFive, Two | One |
| **Parity** | None, Odd, Even, Mark, Space | None |
| **Handshake** | None, XOnXOff, RequestToSend, RequestToSendXOnXOff | None |
| **DTR Enable** | true/false | true |
| **RTS Enable** | true/false | true |
| **Local Echo** | true/false | false |
| **Line Ending** | CRLF (`\r\n`), LF (`\n`), CR (`\r`) | CRLF |

### Data Storage

- **Database**: SQLite stored at `%LocalAppData%\SshManager\sshmanager.db`
- **Logs**: Rolling log files at `%LocalAppData%\SshManager\logs\`
- **Recordings**: ASCIINEMA v2 files at `%LocalAppData%\SshManager\recordings\`
- **Terminal Themes**: Custom themes stored in database

### Terminal Architecture

The terminal uses a bridge pattern to connect SSH.NET or serial ports with xterm.js:

**SSH Connection:**
```
SSH Server  <-->  SSH.NET ShellStream  <-->  SshTerminalBridge
                                                    |
                                                    v
User Input  <-->  xterm.js (WebView2)  <-->  WebTerminalBridge
```

**Serial Connection:**
```
Serial Device  <-->  System.IO.Ports  <-->  SerialTerminalBridge
                                                    |
                                                    v
User Input     <-->  xterm.js (WebView2)  <-->  WebTerminalBridge
```

Both connection types share the same terminal rendering layer (WebTerminalBridge + xterm.js), providing a consistent user experience.

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
3. Select a host and press **Enter**, or
4. Press **Ctrl+K** for Quick Connect, search for a host, and press **Enter**

### Serial Port Quick Connect

1. Click the **Serial** button in the toolbar (or use the menu)
2. Select an available COM port from the dropdown (click **Refresh** to rescan)
3. Configure connection settings:
   - **Baud Rate**: Common values are 9600 or 115200
   - **Data Bits**: Usually 8
   - **Stop Bits**: Usually 1
   - **Parity**: Usually None
   - **Flow Control**: Usually None
4. Optionally enable **Local Echo** for devices that don't echo input
5. Click **Connect** for a temporary connection, or **Connect & Save** to save the configuration

### Controlling Serial Port Signals

While connected to a serial port:
1. Use the **DTR** button to toggle Data Terminal Ready signal (useful for device reset)
2. Use the **RTS** button to toggle Request To Send signal
3. Use **Send Break** to send a break signal to the device (e.g., for entering ROMMON on Cisco devices)

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

### Exporting to SSH Config

1. Go to **Settings** or **File > Export**
2. Select **Export to SSH Config**
3. Choose hosts to export (or select all)
4. Configure export options (comments, groups, ProxyJump style)
5. Click **Export** and choose destination file

### Recording a Terminal Session

1. Connect to a host to open a terminal tab
2. Click the **Record** button or use the keyboard shortcut
3. Perform your terminal operations
4. Click **Stop Recording** when finished
5. Recording is saved in ASCIINEMA v2 format (.cast)

### Playing Back Recordings

1. Go to **View > Recording Browser** or click the recordings icon
2. Select a recording from the list
3. Use playback controls: play/pause, seek, adjust speed (0.5x to 4x)
4. Recordings can be shared and played in external ASCIINEMA players

## Configuration

### Application Settings

Access settings via the gear icon in the toolbar. Key settings include:

| Setting | Description |
|---------|-------------|
| **Terminal Theme** | Select terminal color scheme |
| **Scrollback Buffer** | Number of lines to keep in terminal history |
| **Session Logging** | Enable logging of terminal sessions |
| **Session Recording** | Automatically record terminal sessions |
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

### Serial Port Not Found

**Problem:** COM port doesn't appear in the dropdown

**Solution:**
1. Click **Refresh** to rescan for available ports
2. Check Device Manager to verify the port is recognized by Windows
3. Ensure the USB-to-serial adapter drivers are installed
4. Try unplugging and reconnecting the cable

### Serial Connection Failed

**Problem:** Cannot connect to serial port

**Solution:**
1. Verify the port isn't already in use by another application (PuTTY, Arduino IDE, etc.)
2. Check the baud rate matches the device configuration (common: 9600, 115200)
3. Try different data bits/parity/stop bits settings
4. Some devices require DTR or RTS enabled to communicate

### No Data from Serial Device

**Problem:** Connected but not receiving any data

**Solution:**
1. Enable **Local Echo** to see what you're typing
2. Try pressing Enter to prompt the device
3. Check the line ending setting (some devices expect LF only, others CRLF)
4. Toggle DTR/RTS signals - some devices need these to enable transmission
5. Verify the cable is a data cable, not charge-only (for USB devices)

### Garbled Serial Data

**Problem:** Receiving data but it's unreadable

**Solution:**
1. Verify the baud rate matches the device exactly
2. Check data bits (8 is most common) and parity settings
3. Ensure flow control settings match the device
4. Try a lower baud rate for long cable runs

## Additional Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) - Detailed system architecture
- [GETTING_STARTED.md](./GETTING_STARTED.md) - Step-by-step beginner guide
- [API.md](./API.md) - Terminal services API reference for developers
- [CLAUDE.md](../CLAUDE.md) - Developer reference for AI assistants

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8, WPF |
| UI Library | WPF-UI (Fluent Design) |
| MVVM | CommunityToolkit.Mvvm |
| Database | SQLite via EF Core |
| SSH | SSH.NET |
| Serial | System.IO.Ports + RJCP.SerialPortStream |
| Terminal | xterm.js via WebView2 |
| Logging | Serilog |

## License

[License information to be added]
