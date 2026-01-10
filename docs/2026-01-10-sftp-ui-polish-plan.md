# SFTP Browser UI Polish Plan

**Date:** 2026-01-10
**Status:** Completed
**Priority:** Medium (Quality of Life)

## Overview

Modernize the SFTP browser dialog to feel more polished and aligned with contemporary design standards while maintaining functionality.

---

## Phase 1: Toolbar Refinement ✅

### 1.1 Connection Status Indicator
- [x] Replace bright green "Connected" pill badge with subtle indicator
- [x] Add small green dot icon next to hostname when connected
- [x] Show red dot or disconnected icon when not connected
- [ ] Consider adding connection latency indicator (optional)

**Files modified:**
- `src/SshManager.App/Views/Controls/SftpBrowserControl.xaml`

### 1.2 Button Grouping & Styling
- [x] Group related actions with visual separators:
  - Navigation group: Refresh
  - Transfer group: Upload, Download
  - File operations group: New Folder, Delete, Permissions
  - Connection group: Disconnect
- [x] Icon-only buttons with tooltips for all actions
- [x] Reduce button padding for more compact toolbar
- [x] Added keyboard shortcuts (Ctrl+U Upload, Ctrl+D Download)

### 1.3 Transfer Queue Badge
- [x] Add badge/indicator showing active transfer count
- [x] Position near Download button in transfer group

---

## Phase 2: Panel Headers & Navigation ✅

### 2.1 Panel Labels
- [x] Restyle "Local" and "Remote" headers
- [x] Options:
  - Tab-style headers with subtle background
  - Icon + label combination (Desktop icon for Local, Cloud icon for Remote)
  - Smaller, muted typography with accent underline

**Files modified:**
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`

### 2.2 Breadcrumb Navigation
- [x] Unify styling between Local and Remote panels
- [x] Add hover states to path segments
- [x] Use chevron separators (ChevronRight20 icon)
- [x] Add path copy button (clipboard icon)

**Files modified:**
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/FileBrowserControlBase.cs`

### 2.3 Quick Access Buttons
- [x] **Local drives (C:\, D:\, etc.):**
  - Styled as chip buttons with background
  - HardDrive20 icon with accent color
  - Tooltip shows drive label and free/total space in GB
- [x] **Remote shortcuts (Home, Root, /tmp, etc.):**
  - Matching styling with local drives
  - Home24 icon for Home, Folder24 for other paths
  - Tooltip shows full path

**Files modified:**
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`
- `src/SshManager.App/Converters/BytesToGigabytesConverter.cs` (new)

---

## Phase 3: File List Improvements ✅

### 3.1 Visual Enhancements
- [x] Add subtle row hover highlight (use theme's subtle background)
- [x] Improve selected row styling with accent color
- [x] Add focus indicators for keyboard navigation

### 3.2 File Icons
- [x] Add file type icons based on extension
- [x] Common types: folder, document, image, code, archive, executable
- [x] Use WPF-UI's `SymbolIcon` for consistency

### 3.3 Column Formatting
- [x] **Size column:**
  - Consistent formatting (always KB/MB/GB with 1 decimal)
  - Right-align for easier scanning
  - Show "-" for folders instead of empty
- [x] **Date column:**
  - Friendlier format: "Today 14:30", "Yesterday", "Mon 09:15", "Jan 10", "Jan 10, 2026"
  - Show full timestamp on hover tooltip
- [x] **Permissions column (Remote):**
  - Symbolic display (rwxr-xr-x) with tooltip for octal
  - Monospace font for better alignment

### 3.4 Column Headers
- [x] Add sort indicators (arrow icons)
- [x] Highlight active sort column
- [x] Click-to-sort functionality (toggle ascending/descending)
- [ ] Persist sort preference (deferred to future phase)

**Files modified:**
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`
- `src/SshManager.App/ViewModels/FileItemViewModel.cs`
- `src/SshManager.App/ViewModels/FileBrowserViewModelBase.cs`
- `src/SshManager.App/ViewModels/IFileBrowserViewModel.cs`
- `src/SshManager.App/Converters/SortIndicatorConverter.cs` (new)

---

## Phase 4: General Polish ✅

### 4.1 Panel Divider
- [x] Reduce border/divider weight between panels (already minimal)
- [x] Consider draggable splitter for resizing panels (GridSplitter already exists)
- [x] Add collapse button to hide one panel temporarily

**Files modified:**
- `src/SshManager.App/Views/Controls/SftpBrowserControl.xaml`
- `src/SshManager.App/ViewModels/SftpBrowserViewModel.cs`
- `src/SshManager.App/Converters/CollapsedWidthConverter.cs` (new)

### 4.2 Drag & Drop Feedback
- [x] Highlight valid drop zones when dragging
- [x] Show transfer direction indicator (→ or ←)
- [x] Ghost preview of dragged items
- [x] Invalid drop zone feedback (red highlight or cursor)

**Files modified:**
- `src/SshManager.App/Behaviors/FileDragAdorner.cs` (new)
- `src/SshManager.App/Views/Controls/FileBrowserControlBase.cs`
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`

### 4.3 Transfer Progress Area
- [x] Reserve space at bottom of dialog for transfers (already implemented)
- [x] Show:
  - Current file being transferred
  - Progress bar with percentage
  - Transfer speed
  - Time remaining estimate
- [x] Expandable to show transfer queue (already implemented)
- [x] Minimize when no transfers active (already implemented)

### 4.4 Keyboard Shortcuts
- [x] Add keyboard shortcuts for common actions (already implemented)
- [x] F5 = Refresh
- [x] Ctrl+U = Upload
- [x] Ctrl+D = Download
- [x] Del = Delete
- [x] F2 = Rename
- [x] Show shortcuts in button tooltips

**Files modified:**
- `src/SshManager.App/Views/Controls/SftpBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`

---

## Phase 5: Optional Enhancements ✅

### 5.1 Context Menu
- [x] Right-click context menu with common actions
- [x] Match styling with WPF-UI menus

**Files modified:**
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`

### 5.2 Search/Filter
- [x] Add filter box to quickly find files by name
- [x] Real-time filtering as user types

**Files modified:**
- `src/SshManager.App/Views/Controls/LocalFileBrowserControl.xaml`
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`
- `src/SshManager.App/ViewModels/FileBrowserViewModelBase.cs`
- `src/SshManager.App/ViewModels/IFileBrowserViewModel.cs`

### 5.3 Favorites/Bookmarks
- [x] Allow bookmarking frequently accessed remote paths
- [x] Quick access dropdown

**Files modified:**
- `src/SshManager.App/Views/Controls/RemoteFileBrowserControl.xaml`
- `src/SshManager.App/ViewModels/RemoteFileBrowserViewModel.cs`
- `src/SshManager.Core/Models/AppSettings.cs`

### 5.4 Dual-Pane Sync
- [x] Option to sync navigation between panels
- [x] "Mirror navigation" toggle

**Files modified:**
- `src/SshManager.App/Views/Controls/SftpBrowserControl.xaml`
- `src/SshManager.App/ViewModels/SftpBrowserViewModel.cs`
- `src/SshManager.Core/Models/AppSettings.cs`

---

## Implementation Notes

### WPF-UI Components to Use
- `ui:CardAction` - for grouped buttons
- `ui:SymbolIcon` - for file type icons
- `ui:Badge` - for transfer count indicator
- `ui:InfoBadge` - for status indicators
- `ui:TextBox` with icon - for filter/search

### Color Tokens (from theme)
- Hover: `ControlFillColorSecondary`
- Selected: `AccentFillColorDefaultBrush`
- Subtle text: `TextFillColorSecondaryBrush`
- Borders: `ControlStrokeColorDefaultBrush`

### Files Likely to Modify
```
src/SshManager.App/Views/Dialogs/SftpBrowserDialog.xaml
src/SshManager.App/Views/Dialogs/SftpBrowserDialog.xaml.cs
src/SshManager.App/ViewModels/SftpBrowserViewModel.cs
src/SshManager.App/Converters/ (new converters as needed)
src/SshManager.App/Resources/ (icons if custom needed)
```

---

## Success Criteria

- [x] Toolbar feels organized and less cluttered
- [x] Connection status is clear but not visually loud
- [x] File lists are easy to scan and navigate
- [x] Drag & drop provides clear visual feedback
- [x] Transfer progress is visible without being intrusive
- [x] Overall dialog feels modern and consistent with main app
