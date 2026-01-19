# Quick Start: Velopack Integration

This guide gets you up and running with Velopack in **5 minutes**.

## Prerequisites

Install the Velopack CLI:
```powershell
dotnet tool install -g vpk
```

## Step 1: Update Configuration (One-Time Setup)

### 1.1 Set Your GitHub Repository

Edit `src/SshManager.App/Services/VelopackUpdateService.cs`:

```csharp
var source = new GithubSource(
    repoUrl: "https://github.com/YOUR_USERNAME/sshmanager", // <-- Update this
    accessToken: null,
    prerelease: false);
```

### 1.2 Set Version Metadata

Edit `src/SshManager.App/SshManager.App.csproj` - add these properties:

```xml
<PropertyGroup>
  <!-- ...existing properties... -->
  
  <!-- Version Info -->
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0</AssemblyVersion>
  <FileVersion>1.0.0</FileVersion>
  <Company>SshManager</Company>
  <Product>SshManager</Product>
  <Authors>Your Name</Authors>
</PropertyGroup>
```

## Step 2: Build Your First Release

Run the automated build script:

```powershell
.\build-release.ps1 -Version "1.0.0"
```

This will:
1. ? Build the application in Release mode
2. ? Create a self-contained Windows x64 package
3. ? Package it with Velopack
4. ? Generate installer and update packages

**Output:**
- `releases/SshManager-1.0.0-win-Setup.exe` - Installer for users
- `releases/SshManager-1.0.0-win-full.nupkg` - Update package

## Step 3: Test Locally

Install and test the release:

```powershell
# Run the installer
.\releases\SshManager-1.0.0-win-Setup.exe

# The app installs to: %LocalAppData%\SshManager
```

## Step 4: Publish to GitHub

### 4.1 Create Git Tag

```powershell
git tag v1.0.0
git push origin v1.0.0
```

### 4.2 Create GitHub Release

1. Go to: `https://github.com/YOUR_USERNAME/sshmanager/releases`
2. Click **"Draft a new release"**
3. Choose tag: `v1.0.0`
4. Title: `SshManager v1.0.0`
5. Description: Add release notes
6. Upload files:
   - `SshManager-1.0.0-win-Setup.exe`
   - `SshManager-1.0.0-win-full.nupkg`
7. Click **"Publish release"**

## Step 5: Test Updates

### 5.1 Build Update Version

Update version to 1.0.1 in `SshManager.App.csproj`, then:

```powershell
.\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"
```

This creates a **delta update** (~5-20MB instead of ~80MB).

### 5.2 Publish Update

1. Tag and push: `git tag v1.0.1 && git push origin v1.0.1`
2. Create GitHub release for v1.0.1
3. Upload all three files:
   - `SshManager-1.0.1-win-Setup.exe`
   - `SshManager-1.0.1-win-full.nupkg`
   - `SshManager-1.0.1-win-delta.nupkg`

### 5.3 Test Update in App

1. Open the installed app (v1.0.0)
2. Go to Settings
3. Add the Update tab UI (see VELOPACK_GUIDE.md)
4. Click "Check for Updates"
5. Should detect v1.0.1
6. Download and install

## Common Commands

### Build first release:
```powershell
.\build-release.ps1 -Version "1.0.0"
```

### Build update (with delta):
```powershell
.\build-release.ps1 -Version "1.0.1" -PreviousVersion "1.0.0"
```

### Skip build (re-package only):
```powershell
.\build-release.ps1 -Version "1.0.0" -SkipBuild
```

### Skip clean (faster rebuild):
```powershell
.\build-release.ps1 -Version "1.0.0" -SkipClean
```

## Troubleshooting

### "vpk: command not found"
```powershell
dotnet tool install -g vpk
# Restart terminal
```

### "Update check returns null"
- Verify GitHub release is published (not draft)
- Check tag format: `v1.0.0` (not `1.0.0`)
- Ensure `.nupkg` files are uploaded as release assets

### "Build failed"
- Check .NET 8 SDK is installed: `dotnet --version`
- Verify project builds: `dotnet build src/SshManager.App/SshManager.App.csproj`

## Next Steps

- **Add Update UI**: See `docs/VELOPACK_GUIDE.md` section "Integration with Settings Dialog"
- **Auto-check on startup**: See `docs/VELOPACK_GUIDE.md` section "Automatic Update Checks"
- **CI/CD automation**: See `docs/VELOPACK_GUIDE.md` section "Continuous Integration"

## Resources

- Full guide: `docs/VELOPACK_GUIDE.md`
- Velopack docs: https://docs.velopack.io/
- GitHub: https://github.com/velopack/velopack

---

**You're all set!** ?? Build your release and ship updates to users automatically.
