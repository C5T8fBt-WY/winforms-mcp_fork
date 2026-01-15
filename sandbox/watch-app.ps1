# watch-app.ps1
# Watches for test application changes, builds, deploys to Sandbox mapped folder, and triggers restart.
# Run this from Windows (or via powershell.exe from WSL).
#
# Design: 100ms debounce + concurrency lock
# - Debounce: Wait 100ms after last change before building (lets rapid saves settle)
# - Concurrency: If build is running, queue ONE "next build" (no fixed throttle)
# - Build time is the natural throttle

param(
    [string]$ProjectPath = "$PSScriptRoot\..\src\Rhombus.WinFormsMcp.TestApp\Rhombus.WinFormsMcp.TestApp.csproj",
    [string]$DeployPath = "C:\TransportTest\App",
    [string]$SharedPath = "C:\TransportTest\Shared",
    [int]$DebounceMs = 100
)

$ErrorActionPreference = "Stop"

Write-Host "=== TestApp Sandbox Watcher ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectPath"
Write-Host "Deploy:  $DeployPath"
Write-Host "Shared:  $SharedPath"
Write-Host "Debounce: ${DebounceMs}ms"
Write-Host ""

# Ensure directories exist
if (!(Test-Path $DeployPath)) {
    Write-Host "Creating deploy path: $DeployPath"
    New-Item -ItemType Directory -Force -Path $DeployPath | Out-Null
}
if (!(Test-Path $SharedPath)) {
    Write-Host "Creating shared path: $SharedPath"
    New-Item -ItemType Directory -Force -Path $SharedPath | Out-Null
}

# Concurrency state
$global:buildRunning = $false
$global:buildQueued = $false
$global:debounceTimer = $null

# Function to perform build and deploy
function Invoke-BuildAndDeploy {
    # Check concurrency lock
    if ($global:buildRunning) {
        $global:buildQueued = $true
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Build already running, queued next build" -ForegroundColor Gray
        return
    }

    $global:buildRunning = $true
    $global:buildQueued = $false

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Building TestApp..." -ForegroundColor Yellow

    try {
        # 1. Publish to a temporary directory first to avoid partial updates
        $tempBuildDir = Join-Path $DeployPath "_build_temp"

        # Clean temp dir
        if (Test-Path $tempBuildDir) { Remove-Item $tempBuildDir -Recurse -Force }

        # Build/Publish - framework-dependent (not self-contained)
        # Requires .NET runtime in sandbox (mapped from C:\TransportTest\DotNet)
        dotnet publish $ProjectPath -c Release -r win-x64 --no-self-contained -o $tempBuildDir --nologo -v q

        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed!" -ForegroundColor Red
            return
        }

        Write-Host "Build successful. Deploying..." -ForegroundColor Green

        # 2. Copy files to DeployPath
        $files = Get-ChildItem $tempBuildDir -Recurse
        foreach ($file in $files) {
            $relPath = [System.IO.Path]::GetRelativePath($tempBuildDir, $file.FullName)
            $destPath = Join-Path $DeployPath $relPath

            if ($file.PSIsContainer) {
                if (!(Test-Path $destPath)) { New-Item -ItemType Directory -Path $destPath | Out-Null }
            } else {
                try {
                    Copy-Item $file.FullName $destPath -Force -ErrorAction Stop
                } catch {
                    Write-Warning "Could not copy $relPath (File locked?)"
                }
            }
        }

        # Clean temp
        Remove-Item $tempBuildDir -Recurse -Force -ErrorAction SilentlyContinue

        # 3. Trigger Update (atomic write pattern)
        $triggerFile = Join-Path $SharedPath "app.trigger"
        $triggerTmp = Join-Path $SharedPath "app.trigger.tmp"
        Set-Content -Path $triggerTmp -Value ((Get-Date).ToString("o"))
        Move-Item -Path $triggerTmp -Destination $triggerFile -Force
        Write-Host "App update triggered!" -ForegroundColor Cyan

    } catch {
        Write-Host "Error during build/deploy: $_" -ForegroundColor Red
    } finally {
        $global:buildRunning = $false

        # Check if another build was queued while we were building
        if ($global:buildQueued) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Processing queued build..." -ForegroundColor Gray
            Invoke-BuildAndDeploy
        }
    }
}

# Function to handle debounced file change
function Request-Build {
    # Cancel any pending debounce timer
    if ($global:debounceTimer) {
        $global:debounceTimer.Dispose()
        $global:debounceTimer = $null
    }

    # Start new debounce timer
    $global:debounceTimer = New-Object System.Timers.Timer
    $global:debounceTimer.Interval = $DebounceMs
    $global:debounceTimer.AutoReset = $false

    # Register the elapsed event
    Register-ObjectEvent -InputObject $global:debounceTimer -EventName Elapsed -Action {
        Invoke-BuildAndDeploy
    } | Out-Null

    $global:debounceTimer.Start()
}

# Initial Build (no debounce for initial)
Invoke-BuildAndDeploy

# File System Watcher
$projectDir = Split-Path $ProjectPath -Parent
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $projectDir
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true
$watcher.Filter = "*.*" # Watch all files, filter in action

$action = {
    $path = $Event.SourceEventArgs.FullPath
    $changeType = $Event.SourceEventArgs.ChangeType

    # Filter noise
    if ($path -match "\\bin\\") { return }
    if ($path -match "\\obj\\") { return }
    if ($path -match "\.git") { return }
    if ($path -match "\.vs") { return }
    if ($path -match "tmp") { return }
    if ($path -match "temp") { return }

    Write-Host "`nChange detected: $path ($changeType)" -ForegroundColor Gray

    # Request debounced build
    Request-Build
}

Register-ObjectEvent $watcher "Changed" -Action $action
Register-ObjectEvent $watcher "Created" -Action $action
Register-ObjectEvent $watcher "Deleted" -Action $action
Register-ObjectEvent $watcher "Renamed" -Action $action

Write-Host "Watching for changes in $projectDir..." -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop."

# Keep script running
try {
    while ($true) {
        Start-Sleep -Seconds 1
    }
} finally {
    # Cleanup
    if ($global:debounceTimer) {
        $global:debounceTimer.Dispose()
    }
    Get-EventSubscriber | Unregister-Event -ErrorAction SilentlyContinue
}
