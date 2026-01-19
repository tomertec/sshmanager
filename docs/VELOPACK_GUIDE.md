# Velopack Integration Guide for SshManager

This guide explains how to build, package, and distribute SshManager using Velopack for automatic updates.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Development Setup](#development-setup)
3. [Building Releases](#building-releases)
4. [Publishing Updates](#publishing-updates)
5. [Testing](#testing)
6. [Troubleshooting](#troubleshooting)

## Prerequisites

### Required Tools

1. **.NET 8 SDK** - Already installed for development
2. **Velopack CLI** - Install globally:
   ```powershell
   dotnet tool install -g vpk
   ```

3. **GitHub Account** - For hosting releases (already configured)

### Configuration

The app is already configured with:
- `Velopack` NuGet package (v0.0.942)
- `VelopackUpdateService` for checking and applying updates
- `UpdateViewModel` for UI integration
- Velopack initialization in `App.xaml.cs`

## Development Setup

### 1. Update Repository URL

Edit `src/SshManager.App/Services/VelopackUpdateService.cs`:

```csharp
var source = new GithubSource(
    repoUrl: "https://github.com/tomertec/sshmanager", // <-- Your repo
    accessToken: null, // Public repo (or set GITHUB_TOKEN env var for private)
    prerelease: false); // Set to true to include beta versions
```

### 2. Set Application Metadata

Edit `src/SshManager.App/SshManager.App.csproj` and add:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  <!-- ...existing properties... -->
  
  <!-- Velopack Metadata -->
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0</AssemblyVersion>
  <FileVersion>1.0.0</FileVersion>
  <Company>SshManager</Company>
  <Product>SshManager</Product>
  <Authors>Your Name</Authors>
  <Description>Modern SSH and Serial Port Connection Manager</Description>
  <Copyright>Copyright © 2024</Copyright>
</PropertyGroup>
```

### 3. Verify Icon

Ensure you have an app icon at `src/SshManager.App/Resources/app-icon.ico`.

## Building Releases

### Step 1: Publish the Application

Build a self-contained Windows x64 release:

```powershell
cd C:\Users\gergo\Github\sshmanager

dotnet publish src/SshManager.App/SshManager.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=false `
  -o .\publish\win-x64
```

**Note:** We use `PublishSingleFile=false` because Velopack handles packaging.

### Step 2: Create Velopack Release Package

#### First Release (v1.0.0)

```powershell
vpk pack `
  --packId SshManager `
  --packVersion 1.0.0 `
  --packDir .\publish\win-x64 `
  --mainExe SshManager.App.exe `
  --icon .\src\SshManager.App\Resources\app-icon.ico `
  --outputDir .\releases
```

This creates:
- `releases/SshManager-1.0.0-win-Setup.exe` - Initial installer
- `releases/SshManager-1.0.0-win-full.nupkg` - Full package for updates

#### Subsequent Releases (e.g., v1.0.1)

```powershell
# Publish new version
dotnet publish src/SshManager.App/SshManager.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=false `
  -o .\publish\win-x64

# Create delta update
vpk pack `
  --packId SshManager `
  --packVersion 1.0.1 `
  --packDir .\publish\win-x64 `
  --mainExe SshManager.App.exe `
  --icon .\src\SshManager.App\Resources\app-icon.ico `
  --delta releases\SshManager-1.0.0-win-full.nupkg `
  --outputDir .\releases
```

The `--delta` option creates a smaller update package containing only changed files.

### Step 3: Understand Release Artifacts

After running `vpk pack`, you'll have:

```
releases/
  ??? SshManager-1.0.0-win-Setup.exe      (Initial installer - ~50-100MB)
  ??? SshManager-1.0.0-win-full.nupkg     (Full package for future deltas)
  ??? SshManager-1.0.1-win-Setup.exe      (Updated installer)
  ??? SshManager-1.0.1-win-full.nupkg     (New full package)
  ??? SshManager-1.0.1-win-delta.nupkg    (Small delta update - ~5-20MB)
```

## Publishing Updates

### Option 1: GitHub Releases (Recommended)

1. **Create a GitHub Release:**
   - Go to https://github.com/tomertec/sshmanager/releases
   - Click "Draft a new release"
   - Tag: `v1.0.0` (must start with 'v')
   - Title: `SshManager v1.0.0`
   - Description: Add release notes (these will be shown to users)

2. **Upload Release Assets:**
   - Upload `SshManager-1.0.0-win-Setup.exe` (for new users)
   - Upload `SshManager-1.0.0-win-full.nupkg` (for Velopack)
   - Upload `RELEASES` file (if generated)

3. **Publish the Release:**
   - Click "Publish release"
   - The update will now be available to all users

### Option 2: Custom Update Server

If you want to host updates on your own server:

```csharp
// In VelopackUpdateService.cs, replace GithubSource with:
var source = new HttpUpdateSource(
    baseUrl: "https://yourdomain.com/updates/");
```

Then upload all `.nupkg` files and the `RELEASES` file to your server.

## Testing

### Test in Development

1. **Build and install locally:**
   ```powershell
   vpk pack --packId SshManager --packVersion 0.9.0 --packDir .\publish\win-x64 --mainExe SshManager.App.exe --outputDir .\test-releases
   ```

2. **Run the installer:**
   ```powershell
   .\test-releases\SshManager-0.9.0-win-Setup.exe
   ```

3. **Create a test update:**
   ```powershell
   # Update version in .csproj to 0.9.1
   dotnet publish ...
   vpk pack --packId SshManager --packVersion 0.9.1 --packDir .\publish\win-x64 --mainExe SshManager.App.exe --delta .\test-releases\SshManager-0.9.0-win-full.nupkg --outputDir .\test-releases
   ```

4. **Test update mechanism:**
   - Run the installed app (v0.9.0)
   - Click "Check for Updates" in settings
   - It should detect v0.9.1
   - Download and install the update

### Test Update UI

Add a test menu item to `MainWindow.xaml.cs`:

```csharp
private async void TestUpdateButton_Click(object sender, RoutedEventArgs e)
{
    var updateVm = App.GetService<UpdateViewModel>();
    await updateVm.CheckForUpdatesCommand.ExecuteAsync(null);
}
```

## Integration with Settings Dialog

### Add Update Tab to Settings

Edit `src/SshManager.App/Views/Dialogs/SettingsDialog.xaml`:

```xml
<!-- Add to TabControl -->
<TabItem Header="Updates">
    <StackPanel Margin="20">
        <!-- Current Version -->
        <TextBlock Text="Current Version" FontWeight="SemiBold" Margin="0,0,0,5"/>
        <TextBlock Text="{Binding UpdateViewModel.CurrentVersion}" Margin="0,0,0,20"/>

        <!-- Check for Updates Button -->
        <ui:Button 
            Content="Check for Updates" 
            Command="{Binding UpdateViewModel.CheckForUpdatesCommand}"
            Icon="{ui:SymbolIcon Download24}"
            Appearance="Primary"
            IsEnabled="{Binding UpdateViewModel.IsCheckingForUpdate, Converter={StaticResource InverseBoolConverter}}"
            Margin="0,0,0,10"/>

        <!-- Update Available Panel -->
        <Border 
            Visibility="{Binding UpdateViewModel.UpdateAvailable, Converter={StaticResource BoolToVisibilityConverter}}"
            Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
            BorderBrush="{DynamicResource AccentFillColorDefaultBrush}"
            BorderThickness="2"
            CornerRadius="8"
            Padding="15"
            Margin="0,10,0,0">
            <StackPanel>
                <TextBlock 
                    Text="{Binding UpdateViewModel.AvailableUpdate.Version, StringFormat='New Version Available: v{0}'}"
                    FontWeight="SemiBold"
                    Margin="0,0,0,10"/>

                <!-- Download Progress -->
                <ProgressBar 
                    Value="{Binding UpdateViewModel.DownloadProgress}"
                    Maximum="100"
                    Height="4"
                    Margin="0,0,0,10"
                    Visibility="{Binding UpdateViewModel.IsDownloadingUpdate, Converter={StaticResource BoolToVisibilityConverter}}"/>

                <!-- Action Buttons -->
                <StackPanel Orientation="Horizontal" Spacing="10">
                    <ui:Button
                        Content="Download Update"
                        Command="{Binding UpdateViewModel.DownloadUpdateCommand}"
                        Visibility="{Binding UpdateViewModel.IsUpdateReadyToInstall, Converter={StaticResource InverseBoolToVisibilityConverter}}"
                        Appearance="Primary"/>

                    <ui:Button
                        Content="Install and Restart"
                        Command="{Binding UpdateViewModel.ApplyUpdateCommand}"
                        Visibility="{Binding UpdateViewModel.IsUpdateReadyToInstall, Converter={StaticResource BoolToVisibilityConverter}}"
                        Appearance="Success"/>

                    <ui:Button
                        Content="Dismiss"
                        Command="{Binding UpdateViewModel.DismissUpdateCommand}"/>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- Auto-update Settings -->
        <Separator Margin="0,20,0,20"/>
        <CheckBox 
            Content="Automatically check for updates on startup"
            IsChecked="{Binding Settings.AutoCheckUpdates}"
            Margin="0,0,0,10"/>
        <CheckBox 
            Content="Include pre-release versions"
            IsChecked="{Binding Settings.IncludePrereleaseUpdates}"/>
    </StackPanel>
</TabItem>
```

### Wire Up ViewModel

Edit `src/SshManager.App/Views/Dialogs/SettingsDialog.xaml.cs`:

```csharp
public partial class SettingsDialog : FluentWindow
{
    private readonly SettingsViewModel _viewModel;
    private readonly UpdateViewModel _updateViewModel;

    public SettingsDialog()
    {
        var settingsRepo = App.GetService<ISettingsRepository>();
        var historyRepo = App.GetService<IConnectionHistoryRepository>();
        var credentialCache = App.GetService<ICredentialCache>();
        var themeService = App.GetService<ITerminalThemeService>();
        
        _viewModel = new SettingsViewModel(settingsRepo, historyRepo, credentialCache, themeService);
        _updateViewModel = App.GetService<UpdateViewModel>();
        
        DataContext = new 
        { 
            Settings = _viewModel,
            UpdateViewModel = _updateViewModel
        };

        InitializeComponent();
        
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        
        // Optionally check for updates on settings dialog open
        // await _updateViewModel.CheckForUpdatesCommand.ExecuteAsync(null);
    }
}
```

## Automatic Update Checks

### Check on Startup

Edit `App.xaml.cs` `OnStartup` method:

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    var logger = Log.ForContext<App>();
    logger.Information("Application starting up");

    try
    {
        await _host.StartAsync();
        
        // ...existing initialization code...

        // Show main window
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        
        // Check for updates in background (don't block startup)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000); // Wait 5 seconds after startup
                
                var settingsRepo = _host.Services.GetRequiredService<ISettingsRepository>();
                var settings = await settingsRepo.GetAsync();
                
                if (settings.AutoCheckUpdates) // Add this setting to AppSettings model
                {
                    var updateService = _host.Services.GetRequiredService<IUpdateService>();
                    var update = await updateService.CheckForUpdateAsync();
                    
                    if (update != null)
                    {
                        logger.Information("Update available: v{Version}", update.Version);
                        
                        // Show notification in system tray
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var trayService = _host.Services.GetRequiredService<ISystemTrayService>();
                            trayService.ShowNotification(
                                "Update Available",
                                $"SshManager v{update.Version} is available. Click to download.",
                                () => 
                                {
                                    // Open settings to update tab
                                    var settingsDialog = new SettingsDialog();
                                    settingsDialog.ShowDialog();
                                });
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to check for updates on startup");
            }
        });
        
        logger.Information("Main window displayed, startup complete");
    }
    catch (Exception ex)
    {
        // ...existing error handling...
    }

    base.OnStartup(e);
}
```

## Troubleshooting

### Issue: "Update check returns null"

**Causes:**
1. No GitHub release published yet
2. Release tag doesn't match version format (must be `v1.0.0`)
3. Release assets missing (need `.nupkg` files)
4. Private repository without access token

**Solutions:**
- Verify release exists: https://github.com/tomertec/sshmanager/releases
- Check release tag format: `v1.0.0` (not `1.0.0`)
- Ensure `.nupkg` files are uploaded as assets
- For private repos, set `GITHUB_TOKEN` environment variable

### Issue: "Updates download but fail to apply"

**Causes:**
1. Antivirus blocking Velopack
2. Insufficient permissions
3. App running from non-standard location

**Solutions:**
- Add Velopack to antivirus exclusions
- Run app as administrator (first time)
- Ensure app installed via installer (not run from build folder)

### Issue: "Version mismatch errors"

**Causes:**
1. AssemblyVersion doesn't match package version
2. Multiple versions of app running

**Solutions:**
- Sync versions in `.csproj`:
  ```xml
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0</AssemblyVersion>
  <FileVersion>1.0.0</FileVersion>
  ```
- Close all instances before updating

### Issue: "Development builds show 'Unknown' version"

**Cause:** Velopack versioning only works for installed apps.

**Solution:** This is expected. Version detection works after installation via `.exe` installer.

## Best Practices

### Versioning Strategy

Use **Semantic Versioning** (SemVer):
- **Major** (1.x.x): Breaking changes, major features
- **Minor** (x.1.x): New features, backward compatible
- **Patch** (x.x.1): Bug fixes only

Example progression:
```
1.0.0 ? Initial release
1.0.1 ? Bug fixes
1.1.0 ? New feature (serial port support)
2.0.0 ? Breaking change (new database schema)
```

### Release Notes

Always include release notes in GitHub releases:

```markdown
## What's New in v1.1.0

### Features
- Added serial port connection support
- Improved terminal performance
- New dark theme for terminal

### Bug Fixes
- Fixed SSH key passphrase caching
- Resolved WebView2 memory leak
- Corrected SFTP upload progress

### Breaking Changes
None

### Upgrade Notes
No action required - update will migrate settings automatically.
```

### Delta Updates

Always create delta packages for minor updates:
- Users only download changed files
- Typical delta: 5-20MB vs full package: 50-100MB
- Faster downloads and less bandwidth

```powershell
# Always include --delta for v1.0.1+
vpk pack ... --delta releases\SshManager-1.0.0-win-full.nupkg
```

### Testing Checklist

Before publishing a release:
- [ ] Test installer on clean Windows machine
- [ ] Verify app launches and all features work
- [ ] Test update from previous version
- [ ] Verify delta update works
- [ ] Check release notes are accurate
- [ ] Ensure `.nupkg` files are uploaded to GitHub release
- [ ] Test on Windows 10 and Windows 11

## Advanced Configuration

### Custom Update Channel

Support beta/stable channels:

```csharp
// VelopackUpdateService.cs
public VelopackUpdateService(ILogger<VelopackUpdateService> logger, ISettingsRepository settings)
{
    _logger = logger;
    
    var currentSettings = settings.GetAsync().Result;
    var includePrereleases = currentSettings.IncludePrereleaseUpdates;
    
    var source = new GithubSource(
        repoUrl: "https://github.com/tomertec/sshmanager",
        accessToken: null,
        prerelease: includePrereleases); // User-controlled
    
    _updateManager = new UpdateManager(source);
}
```

### Silent Updates

Auto-download and install updates without user prompt:

```csharp
private async Task AutoUpdateAsync()
{
    var update = await _updateService.CheckForUpdateAsync();
    if (update != null)
    {
        await _updateService.DownloadUpdateAsync(update);
        
        // Apply on next restart (don't interrupt user)
        _logger.Information("Update ready to install on next restart");
    }
}
```

### Rollback Support

Velopack supports rollback to previous version:

```csharp
// In UpdateViewModel
[RelayCommand]
private async Task RollbackAsync()
{
    try
    {
        // This will rollback to the previous version
        _updateManager.ApplyUpdatesAndRestart(toVersion: previousVersion);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to rollback");
    }
}
```

## Continuous Integration

### GitHub Actions Workflow

Create `.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build-and-release:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'
    
    - name: Install Velopack
      run: dotnet tool install -g vpk
    
    - name: Extract version from tag
      id: version
      run: |
        $version = "${env:GITHUB_REF}".replace('refs/tags/v', '')
        echo "VERSION=$version" >> $env:GITHUB_ENV
    
    - name: Publish app
      run: |
        dotnet publish src/SshManager.App/SshManager.App.csproj `
          -c Release `
          -r win-x64 `
          --self-contained `
          -p:PublishSingleFile=false `
          -p:Version=${{ env.VERSION }} `
          -o ./publish/win-x64
    
    - name: Create Velopack release
      run: |
        vpk pack `
          --packId SshManager `
          --packVersion ${{ env.VERSION }} `
          --packDir ./publish/win-x64 `
          --mainExe SshManager.App.exe `
          --icon ./src/SshManager.App/Resources/app-icon.ico `
          --outputDir ./releases
    
    - name: Create GitHub Release
      uses: softprops/action-gh-release@v1
      with:
        files: |
          releases/SshManager-${{ env.VERSION }}-win-Setup.exe
          releases/SshManager-${{ env.VERSION }}-win-full.nupkg
          releases/SshManager-${{ env.VERSION }}-win-delta.nupkg
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

To trigger a release:
```powershell
git tag v1.0.0
git push origin v1.0.0
```

## Summary

You now have:
? Velopack integrated into the app
? Update service for checking and applying updates
? Update UI (ready to add to settings)
? Automatic update checks (optional)
? Complete build and release process
? GitHub Actions automation (optional)

Next steps:
1. Update repository URL in `VelopackUpdateService.cs`
2. Set version in `SshManager.App.csproj`
3. Build and test your first release
4. Publish to GitHub releases
5. Add update UI to settings dialog

Happy shipping! ??
