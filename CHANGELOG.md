# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### SSH Key Management
- **PPK Import Wizard** - Multi-step wizard for batch importing PuTTY private key files
  - Drag-and-drop file selection
  - Automatic PPK file analysis (key type, encryption status, comment)
  - Passphrase entry for encrypted files
  - Configurable output directory
  - Optional re-encryption with new passphrase
  - Add converted keys to SSH agent automatically
  - Track imported keys in managed keys database
- **Key Encryption Service** - Manage SSH private key passphrases
  - Encrypt unencrypted keys with a passphrase
  - Change existing key passphrases
  - Remove encryption from keys
  - Supports RSA and ECDSA keys in PKCS#8 format
  - Automatic backup before modifications
- **Agent Key Service** - Programmatic SSH agent management
  - Add keys to Pageant or OpenSSH Agent
  - Remove keys from OpenSSH Agent
  - Check agent availability status
  - Secure temporary file handling for key content

#### PPK Conversion Enhancements
- **Batch conversion** - Convert multiple PPK files in a single operation
- **Bidirectional conversion** - Convert OpenSSH keys to PPK format (V2 or V3)
- `ConvertToPpkAsync` and `ConvertAndSaveAsPpkAsync` methods for OpenSSH → PPK
- `ConvertBatchToOpenSshAsync` and `ConvertBatchAndSaveAsync` for batch operations

## [1.0.0] - 2026-01-16

### Added

#### Core Features
- Host management with hostname, port, username, and authentication settings
- Group organization for hosts with tagging support
- Embedded xterm.js terminal with WebView2 rendering
- Multiple terminal tabs
- Split panes (horizontal and vertical)
- Quick Connect command palette (Ctrl+K) with fuzzy search

#### Serial Port Connections
- Full COM port support for embedded devices, routers, and network equipment
- Configurable baud rate (300-230400), data bits, stop bits, parity, and handshake
- DTR/RTS signal control
- Local echo mode and configurable line endings (CRLF, LF, CR)
- Serial Quick Connect dialog with port enumeration

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
- Session recording and playback (ASCIINEMA v2 format)
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
- SSH config export to OpenSSH format
- PuTTY session import
- Backup and restore
- OneDrive cloud sync

#### UI
- Modern dark theme with Fluent Design (WPF-UI)
- System tray with quick-connect menu
- Window state persistence
- Connection history tracking

[1.0.0]: https://github.com/tomertec/sshmanager/releases/tag/v1.0.0
