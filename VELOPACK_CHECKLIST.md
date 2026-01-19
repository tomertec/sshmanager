# Velopack Integration Checklist

Use this checklist to track your Velopack integration progress.

## ? Phase 1: Core Setup (Required)

- [ ] **Install Velopack CLI**
  ```powershell
  dotnet tool install -g vpk
  ```

- [ ] **Update Repository URL**
  - File: `src/SshManager.App/Services/VelopackUpdateService.cs`
  - Line: ~23
  - Change: `repoUrl: "https://github.com/YOUR_USERNAME/sshmanager"`

- [ ] **Set Application Version**
  - File: `src/SshManager.App/SshManager.App.csproj`
  - Add to `<PropertyGroup>`:
    ```xml
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0</AssemblyVersion>
    <FileVersion>1.0.0</FileVersion>
    <Company>SshManager</Company>
    <Product>SshManager</Product>
    <Authors>Your Name</Authors>
    ```

- [ ] **Build First Test Release**
  ```powershell
  .\build-release.ps1 -Version "0.9.0"
  ```

- [ ] **Test Installer Locally**
  ```powershell
  .\releases\SshManager-0.9.0-win-Setup.exe
  ```

- [ ] **Verify App Launches**
  - Check: `%LocalAppData%\SshManager` exists
  - Check: Start menu shortcut created
  - Check: App launches successfully

## ? Phase 2: Update UI (Recommended)

- [ ] **Add AppSettings Properties**
  - File: `src/SshManager.Core/Models/AppSettings.cs`
  - Add:
    ```csharp
    public bool AutoCheckUpdates { get; set; } = true;
    public bool IncludePrereleaseUpdates { get; set; } = false;
    ```

- [ ] **Create Update Dialog/Tab**
  - Option A: Add "Updates" tab to Settings Dialog
  - Option B: Create standalone Update Dialog
  - Reference: `docs/VELOPACK_GUIDE.md` ? "Integration with Settings Dialog"

- [ ] **Add Update Button to UI**
  - Location: Settings dialog, Help menu, or system tray
  - Command: `UpdateViewModel.CheckForUpdatesCommand`

- [ ] **Test Update UI**
  - Click "Check for Updates"
  - Verify message shows (no updates available for 0.9.0 if no release published)

## ? Phase 3: First Production Release

- [ ] **Update to v1.0.0**
  - File: `src/SshManager.App/SshManager.App.csproj`
  - Change: `<Version>1.0.0</Version>`

- [ ] **Build Production Release**
  ```powershell
  .\build-release.ps1 -Version "1.0.0"
  ```

- [ ] **Create Git Tag**
  ```powershell
  git tag v1.0.0
  git push origin v1.0.0
  ```

- [ ] **Create GitHub Release**
  - Go to: https://github.com/YOUR_USERNAME/sshmanager/releases
  - Click: "Draft a new release"
  - Tag: v1.0.0
  - Title: SshManager v1.0.0
  - Description: Add release notes

- [ ] **Upload Release Assets**
  - Upload: `SshManager-1.0.0-win-Setup.exe`
  - Upload: `SshManager-1.0.0-win-full.nupkg`

- [ ] **Publish Release**
  - Click: "Publish release" (not draft)

- [ ] **Test Download from GitHub**
  - Download: `SshManager-1.0.0-win-Setup.exe`
  - Install on clean machine
  - Verify: Works correctly

## ? Phase 4: Test Update Flow

- [ ] **Build Update (v1.0.1)**
  - Update: `<Version>1.0.1</Version>` in .csproj
  - Make a small change (e.g., fix typo in UI)
  - Build:
    ```powershell
    .\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"
    ```

- [ ] **Verify Delta Created**
  - Check: `releases\SshManager-1.0.1-win-delta.nupkg` exists
  - Check: Delta size is smaller than full package

- [ ] **Publish v1.0.1 Release**
  - Tag: `v1.0.1`
  - Upload: Setup.exe, full.nupkg, delta.nupkg
  - Publish

- [ ] **Test Update from v1.0.0**
  - Open installed app (v1.0.0)
  - Click "Check for Updates"
  - Verify: Shows v1.0.1 available
  - Click: "Download Update"
  - Verify: Download progress shown
  - Click: "Install and Restart"
  - Verify: App restarts as v1.0.1

## ? Phase 5: Auto-Update (Optional)

- [ ] **Implement Startup Check**
  - File: `src/SshManager.App/App.xaml.cs`
  - Method: `OnStartup`
  - Add: Background update check after 5 seconds
  - Reference: `docs/VELOPACK_GUIDE.md` ? "Automatic Update Checks"

- [ ] **Add System Tray Notification**
  - Show notification when update available
  - Click notification opens Settings ? Updates tab

- [ ] **Test Auto-Check**
  - Launch app
  - Wait 5 seconds
  - Verify: Update check runs in background
  - Verify: Notification shown if update available

## ? Phase 6: CI/CD Automation (Optional)

- [ ] **Create GitHub Actions Workflow**
  - File: `.github/workflows/release.yml`
  - Reference: `docs/VELOPACK_GUIDE.md` ? "Continuous Integration"

- [ ] **Test Workflow**
  - Push tag: `git tag v1.0.2 && git push origin v1.0.2`
  - Verify: GitHub Actions builds and publishes release
  - Verify: Release created with all assets

- [ ] **Add Release Notes Automation**
  - Generate release notes from commits
  - Include changelog in GitHub release description

## ?? Testing Checklist

### Installation Testing
- [ ] Install on Windows 10
- [ ] Install on Windows 11
- [ ] Install as standard user (non-admin)
- [ ] Verify Start menu shortcut
- [ ] Verify uninstaller in Control Panel
- [ ] Uninstall and verify clean removal

### Update Testing
- [ ] Update from previous version
- [ ] Verify settings preserved after update
- [ ] Verify database migrated correctly
- [ ] Verify no file corruption
- [ ] Test update cancellation
- [ ] Test update retry after failure

### Edge Cases
- [ ] Update with app running
- [ ] Update with multiple instances
- [ ] Update with no internet connection
- [ ] Update with antivirus enabled
- [ ] Update on low disk space

## ?? Success Criteria

You've successfully integrated Velopack when:

? Users can install the app via `.exe` installer  
? App checks for updates (manually or automatically)  
? Users can download and install updates in-app  
? Delta updates work (smaller download size)  
? Settings and data preserved after updates  
? No manual intervention required for updates  

## ?? Notes

Use this space to track issues, decisions, or customizations:

```
Date       | Note
-----------|----------------------------------------------------------
2024-XX-XX | Initial setup completed
2024-XX-XX | First release (v1.0.0) published
2024-XX-XX | Update UI added to settings dialog
2024-XX-XX | Auto-update on startup implemented
2024-XX-XX | CI/CD workflow configured
```

---

**Status:** ? Not Started | ?? In Progress | ? Completed

**Current Phase:** _________________________________________

**Blockers:** _____________________________________________

**Next Steps:** ___________________________________________
