# SSH Manager - Feature Roadmap

This document tracks implemented features and ideas for future development.

## Implementation Status Legend

- [x] **Completed** - Fully implemented and working
- [ ] **Not Started** - Planned for future development

---

## Security & Authentication

| Feature | Description | Status |
|---------|-------------|--------|
| **SSH Key Manager** | Built-in key generation (RSA, Ed25519), view/copy public keys, manage keys | [x] Completed |
| **Credential Caching** | Temporarily cache decrypted passwords/passphrases in memory with configurable timeout | [x] Completed |
| **2FA/Keyboard Interactive** | Handle keyboard-interactive auth for servers with two-factor authentication | [x] Completed |
| **SSH Fingerprint Verification** | Store and verify host key fingerprints, warn on changes (MITM protection) | [x] Completed |

## Terminal Enhancements

| Feature | Description | Status |
|---------|-------------|--------|
| **Session Logging** | Record terminal output to file with timestamps, log rotation, ANSI stripping | [x] Completed |
| **Split Panes** | Split terminal view horizontally/vertically for side-by-side sessions | [x] Completed |
| **Find in Terminal** | Search through terminal output (Ctrl+F) | [x] Completed |
| **Broadcast Input** | Type once, send to multiple selected sessions simultaneously | [x] Completed |
| **Snippets/Macros** | Save and quickly execute common commands with categories | [x] Completed |
| **Scrollback Buffer** | Configurable scrollback buffer size | [x] Completed |
| **Custom Terminal Themes** | Multiple built-in themes, custom theme support | [x] Completed |

## Connection Features

| Feature | Description | Status |
|---------|-------------|--------|
| **Port Forwarding** | Local and remote port forwarding with saved profiles | [x] Completed |
| **Jump Host (ProxyJump)** | Chain connections through bastion/jump hosts | [x] Completed |
| **SFTP File Browser** | Integrated file browser with drag-and-drop, remote file editing | [x] Completed |
| **Host Status Monitor** | Background ping to show online/offline status | [x] Completed |
| **Wake-on-LAN** | Send magic packet to wake sleeping machines before connecting | [ ] Not Started |
| **Pre/Post Connect Scripts** | Run local commands before connecting or after disconnect | [ ] Not Started |

## Organization & Productivity

| Feature | Description | Status |
|---------|-------------|--------|
| **Groups** | Organize hosts into collapsible groups | [x] Completed |
| **Connection History** | Track connection history with timestamps | [x] Completed |
| **Tags/Labels** | Multiple tags per host for flexible filtering (e.g., "production", "database") | [ ] Not Started |
| **Favorites/Pinned Hosts** | Quick access section for frequently used connections | [ ] Not Started |
| **Host Profiles** | Reusable profiles for common settings across many hosts | [ ] Not Started |
| **Drag & Drop Reorder** | Reorder hosts and groups via drag and drop | [ ] Not Started |
| **Color-Coded Groups** | Assign colors to groups/environments (red=prod, green=dev) | [ ] Not Started |

## Sync & Backup

| Feature | Description | Status |
|---------|-------------|--------|
| **SSH Config Import** | Import hosts from ~/.ssh/config | [x] Completed |
| **PuTTY Session Import** | Import sessions from Windows Registry | [x] Completed |
| **Automatic Backups** | Scheduled encrypted backups of host configurations | [x] Completed |
| **Cloud Sync** | Sync hosts across devices via OneDrive with conflict resolution | [x] Completed |
| **Export/Import** | Export and import host configurations | [x] Completed |

## UI/UX Improvements

| Feature | Description | Status |
|---------|-------------|--------|
| **System Tray** | Minimize to tray, quick connect menu, double-click to restore | [x] Completed |
| **Dark Theme** | Modern Fluent Design dark theme | [x] Completed |
| **Window State Persistence** | Remember window position and size | [x] Completed |
| **Command Palette** | Ctrl+Shift+P for quick actions (like VS Code) | [ ] Not Started |
| **Tab Tear-Off** | Drag tabs to create new windows | [ ] Not Started |
| **Compact/Dense Mode** | Show more hosts in list view with smaller cards | [ ] Not Started |

## Monitoring & Analytics

| Feature | Description | Status |
|---------|-------------|--------|
| **Server Stats** | Show basic server stats (CPU, memory) in status bar | [x] Completed |
| **Connection Analytics** | Charts showing connection frequency, most-used hosts | [ ] Not Started |
| **Session Duration Tracking** | Track how long sessions are active | [ ] Not Started |
| **Disconnect Alerts** | Notification when a monitored host becomes unreachable | [ ] Not Started |

---

## Summary

**Completed Features: 26**
**Planned Features: 13**

### Priority Suggestions for Future Development

**High Value / Medium Effort:**
1. **Tags/Labels** - Would greatly improve organization for users with many hosts
2. **Favorites/Pinned Hosts** - Quick access to common connections
3. **Color-Coded Groups** - Visual distinction between environments (prod vs dev)

**Medium Value / Low Effort:**
4. **Drag & Drop Reorder** - Quality of life improvement
5. **Compact/Dense Mode** - Better for users with many hosts

**Nice to Have:**
6. **Command Palette** - Power user feature
7. **Wake-on-LAN** - Niche use case but useful for home labs
8. **Pre/Post Connect Scripts** - Advanced automation
9. **Tab Tear-Off** - Complex to implement, limited benefit
10. **Connection Analytics** - Low priority, mainly cosmetic

---

*Last updated: January 2026*
