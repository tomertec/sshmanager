# Velopack Integration Summary

## ? What's Been Done

### 1. Core Integration Files Created

| File | Purpose |
|------|---------|
| `src/SshManager.App/Services/IUpdateService.cs` | Update service interface |
| `src/SshManager.App/Services/VelopackUpdateService.cs` | Velopack implementation |
| `src/SshManager.App/ViewModels/UpdateViewModel.cs` | MVVM update UI logic |

### 2. Configuration Updates

| File | Changes |
|------|---------|
| `src/SshManager.App/App.xaml.cs` | Added Velopack initialization with lifecycle hooks |
| `src/SshManager.App/Infrastructure/ServiceRegistrar.cs` | Registered `IUpdateService` and `UpdateViewModel` in DI |
| `src/SshManager.App/SshManager.App.csproj` | Added Velopack NuGet package v0.0.942 |

### 3. Documentation Created

| Document | Description |
|----------|-------------|
| `docs/VELOPACK_GUIDE.md` | Comprehensive guide (build, deploy, integrate UI) |
| `docs/VELOPACK_QUICKSTART.md` | 5-minute quick start guide |
| `build-release.ps1` | Automated build script for releases |
| `releases/README.md` | Release artifacts reference |

## ?? What You Need to Do

### Required (Before First Build)

1. **Update GitHub Repository URL**
   
   Edit `src/SshManager.App/Services/VelopackUpdateService.cs` line 23:
   ```csharp
   repoUrl: "https://github.com/YOUR_USERNAME/sshmanager", // <-- Change this
   ```

2. **Set Application Version**
   
   Edit `src/SshManager.App/SshManager.App.csproj`, add to `<PropertyGroup>`:
   ```xml
   <Version>1.0.0</Version>
   <AssemblyVersion>1.0.0</AssemblyVersion>
   <FileVersion>1.0.0</FileVersion>
   <Company>SshManager</Company>
   <Product>SshManager</Product>
   <Authors>Your Name</Authors>
   ```

3. **Install Velopack CLI**
   ```powershell
   dotnet tool install -g vpk
   ```

### Optional (Enhanced UX)

4. **Add Update UI to Settings Dialog**
   
   See `docs/VELOPACK_GUIDE.md` ? "Integration with Settings Dialog"
   
   This adds an "Updates" tab with:
   - Current version display
   - Check for updates button
   - Download progress indicator
   - Install and restart button

5. **Enable Auto-check on Startup**
   
   See `docs/VELOPACK_GUIDE.md` ? "Automatic Update Checks"
   
   Checks for updates 5 seconds after app starts (non-blocking)

6. **Setup GitHub Actions CI/CD**
   
   See `docs/VELOPACK_GUIDE.md` ? "Continuous Integration"
   
   Automates building and publishing releases on git tag push

## ?? Quick Start

### Build Your First Release

```powershell
# 1. Install Velopack CLI
dotnet tool install -g vpk

# 2. Build release v1.0.0
.\build-release.ps1 -Version "1.0.0"

# 3. Test the installer
.\releases\SshManager-1.0.0-win-Setup.exe
```

### Publish to GitHub

```powershell
# 1. Create git tag
git tag v1.0.0
git push origin v1.0.0

# 2. Go to GitHub and create release
# Upload: SshManager-1.0.0-win-Setup.exe
# Upload: SshManager-1.0.0-win-full.nupkg
```

### Build and Publish an Update

```powershell
# 1. Update version in .csproj to 1.0.1

# 2. Build with delta
.\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"

# 3. Create GitHub release v1.0.1
# Upload: SshManager-1.0.1-win-Setup.exe
# Upload: SshManager-1.0.1-win-full.nupkg
# Upload: SshManager-1.0.1-win-delta.nupkg (smaller update)
```

## ?? How It Works

### User Perspective

1. **First Install**
   - User downloads `SshManager-1.0.0-win-Setup.exe`
   - Installs to `%LocalAppData%\SshManager`
   - Start menu shortcut created

2. **Update Available**
   - App checks GitHub releases (manual or auto)
   - Finds v1.0.1 available
   - Shows notification or update dialog

3. **Installing Update**
   - Downloads delta package (only changed files, ~10MB)
   - Applies update in background
   - Restarts app automatically
   - App is now v1.0.1

### Developer Perspective

1. **Release Build**
   - Run `build-release.ps1`
   - Publishes app as self-contained win-x64
   - Packages with Velopack into `.exe` installer and `.nupkg` update

2. **GitHub Release**
   - Create git tag `v1.0.0`
   - Upload `.exe` and `.nupkg` to GitHub release
   - Velopack's `GithubSource` automatically finds and downloads updates

3. **Delta Updates**
   - Build v1.0.1 with `--delta` pointing to v1.0.0 full package
   - Velopack creates smaller update containing only diffs
   - Users get faster downloads (5-20MB vs 50-100MB)

## ?? Architecture

```
App Startup
    ?
    ??? VelopackApp.Build().Run()
    ?   ??? OnFirstRun() - First install tasks
    ?   ??? OnAppRestarted() - Post-update tasks
    ?
    ??? Initialize DI Container
        ??? Register IUpdateService ? VelopackUpdateService

Settings Dialog / Update Tab
    ?
    ??? UpdateViewModel
        ??? IUpdateService
            ??? CheckForUpdateAsync()
            ?   ??? GithubSource queries releases
            ?
            ??? DownloadUpdateAsync()
            ?   ??? Downloads .nupkg to temp folder
            ?
            ??? ApplyUpdateAndRestartAsync()
                ??? Installs update and restarts app
```

## ?? Reference Documentation

| Document | When to Use |
|----------|-------------|
| **VELOPACK_QUICKSTART.md** | First-time setup, building releases |
| **VELOPACK_GUIDE.md** | Deep dive, advanced configuration, troubleshooting |
| **build-release.ps1** | Automated release builds |
| **releases/README.md** | Understanding release artifacts |

## ?? Next Steps

### Immediate (Required for Releases)

1. [ ] Update repository URL in `VelopackUpdateService.cs`
2. [ ] Set version in `SshManager.App.csproj`
3. [ ] Install Velopack CLI: `dotnet tool install -g vpk`
4. [ ] Build test release: `.\build-release.ps1 -Version "0.9.0"`
5. [ ] Test installer locally

### Short Term (Enhanced UX)

6. [ ] Add Update UI to Settings Dialog
7. [ ] Test update flow (0.9.0 ? 0.9.1)
8. [ ] Add auto-check on startup
9. [ ] Create first production release (v1.0.0)
10. [ ] Publish to GitHub releases

### Long Term (Automation)

11. [ ] Setup GitHub Actions workflow
12. [ ] Add release notes automation
13. [ ] Consider beta/stable channels
14. [ ] Add telemetry for update success rates

## ? FAQ

**Q: Do users need to install Velopack?**  
A: No, Velopack is embedded in your app. Users just run the `.exe` installer.

**Q: Can I test updates without publishing to GitHub?**  
A: Yes, use `HttpUpdateSource` pointing to a local file server. See VELOPACK_GUIDE.md.

**Q: What happens if an update fails?**  
A: Velopack keeps the previous version. Users can retry or continue using the old version.

**Q: How do I rollback to a previous version?**  
A: Publish a new release with the old version number, or use `ApplyUpdatesAndRestart(toVersion: ...)`.

**Q: Can I update the app while it's running?**  
A: Yes, Velopack downloads in the background. Update applies on next restart or when user clicks "Install Now".

## ?? Troubleshooting

See `docs/VELOPACK_GUIDE.md` ? "Troubleshooting" section for:
- Update check returns null
- Updates fail to apply
- Version mismatch errors
- Development builds show "Unknown" version

## ?? Support

- **Velopack Docs**: https://docs.velopack.io/
- **Velopack GitHub**: https://github.com/velopack/velopack
- **Issues**: File issues in your repository

---

**You're all set to ship updates!** ??

Start with `docs/VELOPACK_QUICKSTART.md` for a 5-minute setup guide.
