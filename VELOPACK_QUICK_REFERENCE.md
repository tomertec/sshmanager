# Velopack Quick Reference Card

## ?? Common Commands

### First-Time Setup
```powershell
# Install Velopack CLI
dotnet tool install -g vpk

# Build first release
.\build-release.ps1 -Version "1.0.0"
```

### Building Releases
```powershell
# Build new version (first release)
.\build-release.ps1 -Version "1.0.0"

# Build update with delta (recommended)
.\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"

# Re-package without rebuilding
.\build-release.ps1 -Version "1.0.0" -SkipBuild

# Skip clean (faster iteration)
.\build-release.ps1 -Version "1.0.0" -SkipClean
```

### Publishing to GitHub
```powershell
# 1. Create and push tag
git tag v1.0.0
git push origin v1.0.0

# 2. Go to GitHub releases
#    https://github.com/YOUR_USERNAME/sshmanager/releases

# 3. Upload files:
#    - SshManager-1.0.0-win-Setup.exe
#    - SshManager-1.0.0-win-full.nupkg
#    - SshManager-1.0.0-win-delta.nupkg (if exists)
```

## ?? File Locations

| File | Location | Purpose |
|------|----------|---------|
| Project File | `src/SshManager.App/SshManager.App.csproj` | Set version here |
| Update Service | `src/SshManager.App/Services/VelopackUpdateService.cs` | Configure repository URL |
| Build Script | `build-release.ps1` | Run to build releases |
| Releases | `releases/` | Output directory for installers |

## ?? Configuration

### Set Version
Edit `src/SshManager.App/SshManager.App.csproj`:
```xml
<Version>1.0.0</Version>
<AssemblyVersion>1.0.0</AssemblyVersion>
<FileVersion>1.0.0</FileVersion>
```

### Set Repository URL
Edit `src/SshManager.App/Services/VelopackUpdateService.cs` line ~29:
```csharp
repoUrl: "https://github.com/YOUR_USERNAME/sshmanager",
```

## ?? Version Numbering

Use **Semantic Versioning**: `MAJOR.MINOR.PATCH`

| Version | When to Use | Example |
|---------|-------------|---------|
| **Major** (1.x.x) | Breaking changes | New database schema |
| **Minor** (x.1.x) | New features | Serial port support added |
| **Patch** (x.x.1) | Bug fixes only | Fix crash on startup |

**Example progression:**
```
1.0.0 ? Initial release
1.0.1 ? Bug fixes
1.1.0 ? New feature
2.0.0 ? Breaking change
```

## ?? Release Artifacts

After running `build-release.ps1`, you get:

| File | Size | Purpose |
|------|------|---------|
| `SshManager-X.Y.Z-win-Setup.exe` | ~80 MB | Installer for new users |
| `SshManager-X.Y.Z-win-full.nupkg` | ~76 MB | Full update package |
| `SshManager-X.Y.Z-win-delta.nupkg` | ~10 MB | Delta update (only changed files) |

**Upload all three files to GitHub releases**

## ? Workflow

### For First Release (v1.0.0)

1. Set version in `.csproj`
2. `.\build-release.ps1 -Version "1.0.0"`
3. Test installer: `.\releases\SshManager-1.0.0-win-Setup.exe`
4. Create git tag: `git tag v1.0.0 && git push origin v1.0.0`
5. Create GitHub release and upload files
6. Publish release ?

### For Updates (v1.0.1+)

1. Make code changes
2. Bump version in `.csproj`
3. `.\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"`
4. Test installer
5. Create git tag and publish to GitHub
6. Users get automatic update notification! ?

## ?? Testing

### Test Installer
```powershell
.\releases\SshManager-X.Y.Z-win-Setup.exe
```

Installs to: `%LocalAppData%\SshManager`

### Test Update
1. Install older version (e.g., 1.0.0)
2. Build and publish newer version (e.g., 1.0.1)
3. Open app ? Settings ? Updates ? Check for Updates
4. Should detect 1.0.1 and offer to download/install

## ?? Common Issues

| Problem | Solution |
|---------|----------|
| "vpk not found" | `dotnet tool install -g vpk` and restart terminal |
| "Update check returns null" | Verify GitHub release is published (not draft) |
| Build fails | Run `dotnet clean && dotnet build` |
| Version shows "Unknown" | Normal in development, works after installation |

## ?? Documentation

| Document | Use Case |
|----------|----------|
| **VELOPACK_QUICKSTART.md** | First-time setup (5 min) |
| **VELOPACK_GUIDE.md** | Complete guide with all features |
| **VELOPACK_CHECKLIST.md** | Track your progress |
| **VELOPACK_INTEGRATION.md** | Summary and FAQ |

## ?? Quick Checklist

Before first release:
- [ ] `dotnet tool install -g vpk`
- [ ] Update repository URL in `VelopackUpdateService.cs`
- [ ] Set version in `.csproj` to `1.0.0`
- [ ] Run `.\build-release.ps1 -Version "1.0.0"`
- [ ] Test installer works
- [ ] Push git tag `v1.0.0`
- [ ] Create GitHub release and upload files
- [ ] Publish release

For updates:
- [ ] Bump version in `.csproj`
- [ ] `.\build-release.ps1 -Version "X.Y.Z" -PreviousVersion "A.B.C"`
- [ ] Test update flow
- [ ] Push git tag
- [ ] Upload to GitHub release

## ?? Pro Tips

1. **Always use delta updates** for v1.0.1+ (saves bandwidth)
2. **Test on clean VM** before publishing
3. **Keep full.nupkg files** - needed for future deltas
4. **Tag format must be** `vX.Y.Z` (with 'v' prefix)
5. **GitHub release must be published** (not draft)

## ?? Useful Links

- Velopack Docs: https://docs.velopack.io/
- GitHub Releases: https://github.com/YOUR_USERNAME/sshmanager/releases
- Installed app location: `%LocalAppData%\SshManager`
- Logs: `%LocalAppData%\SshManager\logs\`

---

**Keep this card handy for quick reference!** ??
