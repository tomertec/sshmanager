# SshManager Release Build Script
# This script automates building and packaging releases with Velopack

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$PreviousVersion = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipClean = $false
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectPath = "src\SshManager.App\SshManager.App.csproj"
$PublishDir = "publish\win-x64"
$ReleasesDir = "releases"
$PackId = "SshManager"
$MainExe = "SshManager.App.exe"
$Icon = "src\SshManager.App\Resources\app-icon.ico"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "SshManager Release Build" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean previous build
if (-not $SkipClean) {
    Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow
    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
        Write-Host "  ? Cleaned publish directory" -ForegroundColor Green
    }
} else {
    Write-Host "[1/4] Skipping clean (--SkipClean specified)" -ForegroundColor Yellow
}

# Step 2: Build and publish
if (-not $SkipBuild) {
    Write-Host "[2/4] Building and publishing application..." -ForegroundColor Yellow
    
    dotnet publish $ProjectPath `
        -c Release `
        -r win-x64 `
        --self-contained `
        -p:PublishSingleFile=false `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version `
        -p:FileVersion=$Version `
        -o $PublishDir
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ? Build failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "  ? Build completed successfully" -ForegroundColor Green
} else {
    Write-Host "[2/4] Skipping build (--SkipBuild specified)" -ForegroundColor Yellow
}

# Step 3: Create releases directory
Write-Host "[3/4] Preparing release directory..." -ForegroundColor Yellow
if (-not (Test-Path $ReleasesDir)) {
    New-Item -ItemType Directory -Path $ReleasesDir | Out-Null
    Write-Host "  ? Created releases directory" -ForegroundColor Green
} else {
    Write-Host "  ? Releases directory exists" -ForegroundColor Green
}

# Step 4: Package with Velopack
Write-Host "[4/4] Packaging with Velopack..." -ForegroundColor Yellow

# Check if vpk is installed
try {
    vpk --version | Out-Null
} catch {
    Write-Host "  ? Velopack CLI (vpk) not found!" -ForegroundColor Red
    Write-Host "  Install with: dotnet tool install -g vpk" -ForegroundColor Yellow
    exit 1
}

# Build vpk command
$vpkArgs = @(
    "pack",
    "--packId", $PackId,
    "--packVersion", $Version,
    "--packDir", $PublishDir,
    "--mainExe", $MainExe,
    "--icon", $Icon,
    "--outputDir", $ReleasesDir
)

# Add delta if previous version specified
if ($PreviousVersion) {
    $deltaNupkg = Join-Path $ReleasesDir "$PackId-$PreviousVersion-win-full.nupkg"
    
    if (Test-Path $deltaNupkg) {
        Write-Host "  Creating delta update from v$PreviousVersion..." -ForegroundColor Cyan
        $vpkArgs += "--delta"
        $vpkArgs += $deltaNupkg
    } else {
        Write-Host "  ? Previous version nupkg not found: $deltaNupkg" -ForegroundColor Yellow
        Write-Host "  Creating full package only (no delta)" -ForegroundColor Yellow
    }
}

# Execute vpk
& vpk @vpkArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ? Velopack packaging failed!" -ForegroundColor Red
    exit 1
}

Write-Host "  ? Velopack packaging completed" -ForegroundColor Green
Write-Host ""

# Summary
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Build Summary" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

$setupExe = Join-Path $ReleasesDir "$PackId-$Version-win-Setup.exe"
$fullNupkg = Join-Path $ReleasesDir "$PackId-$Version-win-full.nupkg"
$deltaNupkg = Join-Path $ReleasesDir "$PackId-$Version-win-delta.nupkg"

if (Test-Path $setupExe) {
    $setupSize = (Get-Item $setupExe).Length / 1MB
    Write-Host "? Setup Installer: $setupExe" -ForegroundColor Green
    Write-Host "  Size: $($setupSize.ToString('0.00')) MB" -ForegroundColor Gray
}

if (Test-Path $fullNupkg) {
    $fullSize = (Get-Item $fullNupkg).Length / 1MB
    Write-Host "? Full Package: $fullNupkg" -ForegroundColor Green
    Write-Host "  Size: $($fullSize.ToString('0.00')) MB" -ForegroundColor Gray
}

if (Test-Path $deltaNupkg) {
    $deltaSize = (Get-Item $deltaNupkg).Length / 1MB
    $savings = (1 - ($deltaSize / $fullSize)) * 100
    Write-Host "? Delta Package: $deltaNupkg" -ForegroundColor Green
    Write-Host "  Size: $($deltaSize.ToString('0.00')) MB (saves $($savings.ToString('0'))%)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Test the installer:" -ForegroundColor White
Write-Host "   .\releases\$PackId-$Version-win-Setup.exe" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Create GitHub Release:" -ForegroundColor White
Write-Host "   git tag v$Version" -ForegroundColor Gray
Write-Host "   git push origin v$Version" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Upload to GitHub:" -ForegroundColor White
Write-Host "   - Go to: https://github.com/tomertec/sshmanager/releases" -ForegroundColor Gray
Write-Host "   - Draft new release with tag v$Version" -ForegroundColor Gray
Write-Host "   - Upload: $PackId-$Version-win-Setup.exe" -ForegroundColor Gray
Write-Host "   - Upload: $PackId-$Version-win-full.nupkg" -ForegroundColor Gray
if (Test-Path $deltaNupkg) {
    Write-Host "   - Upload: $PackId-$Version-win-delta.nupkg" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Build completed successfully! ??" -ForegroundColor Green
