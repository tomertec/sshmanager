# Getting Started with SshManager

This guide walks you through setting up SshManager and connecting to your first SSH host. By the end, you'll have a working terminal session.

## Prerequisites

Before you begin, make sure you have:

- **Windows 10 or 11** (64-bit)
- **.NET 8.0 SDK** - [Download from Microsoft](https://dotnet.microsoft.com/download/dotnet/8.0)
- **An SSH server to connect to** - This could be a Linux server, router, or any device running SSH

To verify .NET is installed, open a terminal and run:
```bash
dotnet --version
```
You should see `8.0.x` or higher.

## Step 1: Build and Run

Open a terminal in the project directory and run:

```bash
# Build the application
dotnet build SshManager.sln

# Run the application
dotnet run --project src/SshManager.App/SshManager.App.csproj
```

The main window should appear with a split layout:
- **Left panel**: Host list with a sample "Sample Host" entry
- **Right panel**: Empty terminal area (tabs appear when you connect)

## Step 2: Add Your First Host

Let's add a real SSH host to connect to.

### Using the Add Host Dialog

1. **Click the + button** in the toolbar, or right-click in the host list and select "Add Host"

2. **Fill in the connection details:**

   | Field | Description | Example |
   |-------|-------------|---------|
   | **Display Name** | A friendly name for this host | `My Server` |
   | **Hostname** | IP address or domain name | `192.168.1.100` or `server.example.com` |
   | **Port** | SSH port (usually 22) | `22` |
   | **Username** | Your SSH username | `admin` |

3. **Select authentication method:**

   - **SSH Agent (Default)**: Uses keys from `~/.ssh/` or Windows OpenSSH agent
   - **Private Key File**: Browse to select a specific key file (e.g., `id_rsa`)
   - **Password**: Enter and save an encrypted password

4. **Click Save**

Your new host appears in the host list.

### Understanding Authentication Types

| Type | When to Use | How It Works |
|------|-------------|--------------|
| **SSH Agent** | You have SSH keys set up in `~/.ssh/` | Automatically uses existing keys from Pageant or Windows OpenSSH Agent |
| **Private Key File** | You have a key file in a custom location | You specify the path to the private key (with optional passphrase) |
| **Password** | The server uses password authentication | Password is encrypted with Windows DPAPI and stored locally |
| **Kerberos** | Your organization uses Active Directory | Uses Windows domain credentials for authentication |

**Tip**: SSH Agent is the most secure option as it doesn't store credentials in the app. If you're using Windows OpenSSH Agent or Pageant, your keys are automatically detected.

## Step 3: Connect to Your Host

There are several ways to connect:

1. **Double-click** the host in the list
2. **Right-click** the host and select "Connect"
3. **Select** the host and press **Enter**

### What Happens When You Connect

1. A new **terminal tab** opens in the right panel
2. The application establishes the SSH connection
3. You see the remote system's login banner or prompt
4. You can now type commands!

### First Connection: Host Key Verification

On first connection to a new host, you'll see a dialog asking to verify the host's fingerprint. This is a security feature:

1. **Compare the fingerprint** with what you expect (check with your server admin if unsure)
2. **Click "Accept"** to trust this host
3. The fingerprint is saved; you won't be asked again unless it changes

**Warning**: If you see this dialog for a host you've connected to before, the server's key may have changed. This could indicate a security issue.

## Step 4: Using the Terminal

Once connected, you have a full terminal session:

### Basic Usage

- **Type commands** and press Enter to execute
- **Scroll** with mouse wheel or scrollbar
- **Select text** by clicking and dragging
- **Copy**: Ctrl+C (when text is selected)
- **Paste**: Ctrl+V

### Terminal Features

| Feature | How to Access |
|---------|---------------|
| **New Tab** | Ctrl+T or right-click host |
| **Close Tab** | Click X on tab, or type `exit` |
| **Switch Tabs** | Click tab or Ctrl+Tab |
| **Search** | Ctrl+F (opens search overlay) |
| **Clear Terminal** | Right-click in terminal > Clear |
| **Change Theme** | Settings > Terminal Theme |

### Full Application Compatibility

The terminal supports complex terminal applications:

- **vim/nano**: Text editors work correctly
- **htop/top**: Process monitors render properly
- **tmux/screen**: Terminal multiplexers work
- **docker**: Container management works
- **colored output**: ls --color, git diff, etc.

## Step 5: Organize with Groups

If you have many hosts, organize them into groups:

### Creating a Group

1. Right-click in the host list
2. Select "Add Group"
3. Enter a name (e.g., "Production", "Development", "Home Lab")
4. Click Save

### Moving Hosts to Groups

1. Right-click a host
2. Select "Move to Group"
3. Choose the target group

### Expanding/Collapsing Groups

- Click the arrow next to a group name to expand/collapse
- Groups remember their state between sessions

## Step 6: Customize Settings

Access settings via the **gear icon** in the toolbar.

### Key Settings

| Setting | What It Does |
|---------|--------------|
| **Terminal Theme** | Change terminal colors (dark themes, solarized, etc.) |
| **Scrollback Buffer** | How many lines of history to keep |
| **Credential Caching** | Remember passwords/passphrases temporarily |
| **Session Logging** | Save terminal output to files |

### Recommended First-Time Settings

1. **Choose a terminal theme** you like
2. **Enable credential caching** if you connect frequently (with timeout)
3. **Set scrollback buffer** to a comfortable size (default 10000 is usually good)

## Common Workflows

### Connecting to Multiple Hosts

1. Connect to first host (creates tab)
2. Double-click another host (creates second tab)
3. Click tabs to switch between sessions

### Using Split Panes

View multiple terminals side by side:

1. Right-click a terminal tab
2. Select "Split Horizontal" or "Split Vertical"
3. Drag tabs between panes

**Keyboard shortcuts:**
- Ctrl+Shift+H: Split horizontal
- Ctrl+Shift+V: Split vertical

### Importing Existing Hosts

**From SSH Config (`~/.ssh/config`):**
1. Settings > Import > SSH Config
2. Select your config file
3. Choose hosts to import
4. Click Import

**From PuTTY:**
1. Settings > Import > PuTTY Sessions
2. Select sessions to import
3. Click Import

### Using Command Snippets

Save frequently used commands:

1. Open Snippets Manager (toolbar or Ctrl+Shift+S)
2. Click Add
3. Enter name, command, and optional category
4. Save

To use a snippet:
1. Right-click in terminal
2. Select "Insert Snippet"
3. Choose your snippet

## Troubleshooting First Connection

### "Connection refused"

- **Check**: Is SSH running on the server? (`sudo systemctl status ssh`)
- **Check**: Is the port correct? (default is 22)
- **Check**: Is the server's firewall allowing SSH?

### "Authentication failed"

- **Check**: Is the username correct?
- **Check**: For password auth, is the password correct?
- **Check**: For key auth, is the key authorized on the server?

### "Host key verification failed"

- The server's key has changed since you last connected
- This could indicate a security issue or a server reinstall
- If expected, remove the old fingerprint from Settings > Host Keys

### Terminal shows garbled text

- Try a different terminal theme
- The server may be sending unexpected escape sequences
- Check the server's `$TERM` environment variable

### Connection drops frequently

- Check network stability
- Consider using `tmux` or `screen` on the server for persistent sessions
- Check if the server has an SSH timeout configured
- Enable **Auto-Reconnect** in Settings to automatically reconnect on drops
- Adjust the **Keep-Alive Interval** in host settings to prevent idle disconnections

## Next Steps

Now that you're connected, explore more features:

- **[SFTP Browser](./README.md#sftp-browser)**: Transfer files graphically
- **[Port Forwarding](./README.md#port-forwarding)**: Tunnel ports through SSH
- **[ProxyJump](./README.md#proxyjump--jump-hosts)**: Connect through bastion hosts
- **[Visual Tunnel Builder](./README.md#using-the-visual-tunnel-builder)**: Create complex tunnel configurations visually
- **[Session Recording](./README.md#recording-a-terminal-session)**: Record and playback terminal sessions
- **[Backup & Sync](./README.md#importexport)**: Backup your hosts or sync across devices
- **[Serial Ports](./README.md#serial-port-quick-connect)**: Connect to COM ports for embedded devices

## Quick Reference Card

| Action | Shortcut/Method |
|--------|-----------------|
| Add host | Toolbar + or right-click > Add Host |
| Connect | Double-click or Enter |
| Quick Connect | Ctrl+K |
| New tab | Ctrl+T |
| Close tab | Ctrl+W or click X |
| Switch tabs | Ctrl+Tab or click |
| Search terminal | Ctrl+F |
| Copy | Ctrl+C (with selection) |
| Paste | Ctrl+V |
| Split horizontal | Ctrl+Shift+H |
| Split vertical | Ctrl+Shift+V |
| Zoom in | Ctrl+Plus |
| Zoom out | Ctrl+Minus |
| Reset zoom | Ctrl+0 |
| Settings | Toolbar gear icon |
| Snippets | Ctrl+Shift+S |
| Start recording | Right-click > Record Session |
| Toggle broadcast | Right-click > Broadcast Input |

## Getting Help

- Check the [README](./README.md) for feature documentation
- Check the [Architecture Guide](./ARCHITECTURE.md) for technical details
- Look at logs in `%LocalAppData%\SshManager\logs\` for error details
