# Release Artifacts

This directory contains packaged releases built with Velopack.

## File Types

### Setup Installer (`.exe`)
**Example:** `SshManager-1.0.0-win-Setup.exe`

- Used by **new users** to install the application
- Contains the full application
- Creates Start Menu shortcuts, uninstaller entry
- Typical size: 50-100 MB

**Distribution:** Upload to GitHub Releases for users to download

### Full Package (`.nupkg`)
**Example:** `SshManager-1.0.0-win-full.nupkg`

- Used by Velopack for **update distribution**
- Contains complete application files
- Required for creating delta updates for future versions
- **Keep this file** - needed for `--delta` when building next version

**Distribution:** Upload to GitHub Releases (Velopack downloads it automatically)

### Delta Package (`.nupkg`)
**Example:** `SshManager-1.0.1-win-delta.nupkg`

- Smaller update package containing **only changed files**
- Created when using `--delta` flag with previous version's full package
- Typical size: 5-20 MB (much smaller than full package)
- Users download this for updates instead of full package

**Distribution:** Upload to GitHub Releases alongside full package

## Version History

Keep a log of releases here:

```
v1.0.0 (2024-01-15)
??? SshManager-1.0.0-win-Setup.exe (78.5 MB)
??? SshManager-1.0.0-win-full.nupkg (76.2 MB)

v1.0.1 (2024-01-22)
??? SshManager-1.0.1-win-Setup.exe (78.8 MB)
??? SshManager-1.0.1-win-full.nupkg (76.5 MB)
??? SshManager-1.0.1-win-delta.nupkg (12.3 MB)
```

## Important Notes

1. **Never delete full packages** - they're needed for creating deltas
2. **Upload all files** to GitHub Releases for each version
3. **Tag format** must be `vX.Y.Z` (e.g., `v1.0.0`)
4. Users on v1.0.0 updating to v1.0.1 will download the **delta** package (12 MB) instead of the full package (76 MB)

## Build Command Reference

Build a new release:
```powershell
.\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"
```

Build without creating delta:
```powershell
.\build-release.ps1 -Version "1.0.0"
```
