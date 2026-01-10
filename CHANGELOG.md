# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-01-10

### Added

#### Core Features
- Host management with hostname, port, username, and authentication settings
- Group organization for hosts
- Embedded xterm.js terminal with WebView2 rendering
- Multiple terminal tabs
- Split panes (horizontal and vertical)

#### Authentication
- SSH Agent support (keys from `~/.ssh/` directory)
- Private key file authentication
- Password authentication with DPAPI encryption
- Keyboard-interactive authentication (2FA support)
- SSH host fingerprint verification

#### Security
- DPAPI password encryption (Windows user-specific)
- Secure in-memory credential caching with configurable timeout
- Session lock credential clearing

#### Terminal Features
- Full VT100/ANSI escape sequence support
- Session logging to file
- Find in terminal (Ctrl+F)
- Broadcast input to multiple sessions
- Command snippets/macros
- Configurable scrollback buffer
- Multiple terminal themes

#### Connection Features
- Local and remote port forwarding
- Jump host (ProxyJump) support
- SFTP file browser with drag-and-drop
- Remote file editor
- Host status monitoring (ping)

#### Import/Export
- SSH config import (`~/.ssh/config`)
- PuTTY session import
- Backup and restore
- OneDrive cloud sync

#### UI
- Modern dark theme with Fluent Design (WPF-UI)
- System tray with quick-connect menu
- Window state persistence
- Connection history tracking

[1.0.0]: https://github.com/YOUR_USERNAME/sshmanager/releases/tag/v1.0.0
