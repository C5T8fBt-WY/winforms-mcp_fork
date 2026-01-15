# Sandbox Hot-Reload Development Workflow - Design

## 1. Overview

This design formalizes the hot-reload architecture for developing the WinForms MCP server AND test applications inside Windows Sandbox. The system uses a two-sided polling architecture: a host-side file watcher that builds and deploys code, and a sandbox-side bootstrap script that monitors for updates and restarts processes.

**Hot-Reloadable Components**:
1. **MCP Server** - The automation server being developed
2. **Test Application** - The WinForms app being automated (e.g., TestApp, CAD app)

**Why this approach?**
- Named pipes don't cross the sandbox VM boundary
- Shared folders are the only reliable communication channel
- Polling is simple, debuggable, and sufficient for development workflows
- Both server and app can be iterated simultaneously

**Key Design Decisions**:
1. **Pre-downloaded .NET Runtime** - Download once to host, map read-only into sandbox. Enables framework-dependent builds (2s vs 8s).
2. **Debounce + Concurrency Lock** - 100ms debounce to let rapid changes settle. Build time is natural throttle; if build running, queue exactly one "next build".
3. **Windows Job Object** - Use Job Object with `KillOnJobClose` for subprocess management. Windows automatically kills child processes when bootstrap exits.
4. **Framework-Dependent Builds** - Smaller output (~5MB vs 80MB), faster builds, faster copies.
5. **Atomic Trigger Files** - Write to `.tmp`, rename to final name. Prevents partial-read race condition.
6. **FileSystemWatcher + Fallback Poll** - FSW for instant trigger detection, 20s poll as safety net.
7. **PID Tracking** - Track PIDs of started processes for precise hot-reload control.

## 2. Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│ HOST (Windows)                                                          │
│                                                                         │
│  ┌──────────────────────┐     ┌──────────────────────┐                 │
│  │ watch-dev.ps1        │     │ watch-app.ps1        │                 │
│  │ (MCP Server)         │     │ (Test Application)   │                 │
│  │                      │     │                      │                 │
│  │ • Watch Server/      │     │ • Watch TestApp/     │                 │
│  │ • Deploy to Phase1   │     │ • Deploy to AppDir   │                 │
│  │ • server.trigger     │     │ • app.trigger        │                 │
│  └──────────────────────┘     └──────────────────────┘                 │
│            │                           │                                │
│            ▼                           ▼                                │
│  ┌──────────────────────────────────────────────────────────────┐      │
│  │ C:\TransportTest\                                             │      │
│  │                                                               │      │
│  │  Server\          App\            DotNet\        Shared\        │      │
│  │  ├── Server.exe   ├── TestApp.exe ├── dotnet.exe ├── mcp-ready  │      │
│  │  ├── *.dll        ├── *.dll       └── shared/    ├── server.trigger   │
│  │  └── bootstrap    └── ...            (runtime)   ├── app.trigger│      │
│  │                                                  ├── shutdown   │      │
│  │                                                  ├── request-*  │      │
│  │                                                  └── response-* │      │
│  └──────────────────────────────────────────────────────────────┘      │
│            │              │              │               │              │
│            │ r/o          │ r/o          │ r/o           │ r/w          │
└────────────┼───────────────────┼───────────────────┼────────────────────┘
             │                   │                   │
┌────────────┼───────────────────┼───────────────────┼────────────────────┐
│ SANDBOX VM ▼                   ▼                   ▼                    │
│  ┌──────────────────────────────────────────────────────────────┐      │
│  │ C:\Server\     C:\App\       C:\DotNet\      C:\Shared\        │      │
│  │ (read-only)    (read-only)   (read-only)     (read-write)      │      │
│  └──────────────────────────────────────────────────────────────┘      │
│       │ copy                │ copy                 ▲                   │
│       ▼                     ▼                      │                   │
│  ┌─────────────────┐  ┌─────────────────┐         │                   │
│  │ C:\LocalServer\ │  │ C:\LocalApp\    │         │                   │
│  │                 │  │                 │         │                   │
│  │ Server.exe ─────┼──┼─ TestApp.exe ───┼─────────┘                   │
│  └─────────────────┘  └─────────────────┘                             │
│       ▲                     ▲                                          │
│       │ manages             │ manages                                  │
│  ┌──────────────────────────────────────────────────┐                 │
│  │ bootstrap.ps1                                     │                 │
│  │                                                   │                 │
│  │ • Start server + app on boot                      │                 │
│  │ • Poll for server.trigger → restart server        │                 │
│  │ • Poll for app.trigger → restart app              │                 │
│  │ • Poll for shutdown.signal → clean exit           │                 │
│  └──────────────────────────────────────────────────┘                 │
└─────────────────────────────────────────────────────────────────────────┘
```

## 3. Components and Interfaces

### 3.1 Host-Side Watchers

Both watchers share the same structure but target different projects:

#### 3.1.1 watch-dev.ps1 (MCP Server)

**Responsibility**: Watch MCP server source, build, deploy, trigger updates

**Parameters**:
| Parameter | Default | Description |
|-----------|---------|-------------|
| `$ProjectPath` | `..\..\src\...\Server.csproj` | Server project to build |
| `$DeployPath` | `C:\TransportTest\Server` | Server deployment target |
| `$SharedPath` | `C:\TransportTest\Shared` | Communication folder |
| `$TriggerName` | `server.trigger` | Trigger file name |

#### 3.1.2 watch-app.ps1 (Test Application)

**Responsibility**: Watch test application source, build, deploy, trigger updates

**Parameters**:
| Parameter | Default | Description |
|-----------|---------|-------------|
| `$ProjectPath` | `..\..\src\...\TestApp.csproj` | App project to build |
| `$DeployPath` | `C:\TransportTest\App` | App deployment target |
| `$SharedPath` | `C:\TransportTest\Shared` | Communication folder |
| `$TriggerName` | `app.trigger` | Trigger file name |

#### 3.1.3 Shared Watcher Logic

**State Machine** (Debounce + Concurrency Lock):
```
IDLE → (file change) → DEBOUNCING (100ms)
                              │
                    (more changes) → reset debounce timer
                              │
                    (no changes for 100ms)
                              │
                    (build already running?)
                              │
                    NO → BUILDING → DEPLOYING → TRIGGERING → check queue → IDLE
                              │
                    YES → queue one "next build" → IDLE
```

**Debounce + Concurrency Lock Logic**:
```powershell
$debounceMs = 100        # Wait for rapid changes to settle
$debounceTimer = $null
$buildInProgress = $false
$buildQueued = $false

# On file change:
if ($debounceTimer) { $debounceTimer.Stop() }
$debounceTimer = New-Object System.Timers.Timer($debounceMs)
$debounceTimer.AutoReset = $false
$debounceTimer.Add_Elapsed({
    if ($buildInProgress) {
        # Build running - queue exactly one next build
        $buildQueued = $true
        return
    }

    $buildInProgress = $true
    Invoke-BuildAndDeploy
    $buildInProgress = $false

    # If changes happened during build, do one more
    if ($buildQueued) {
        $buildQueued = $false
        Invoke-BuildAndDeploy
    }
})
$debounceTimer.Start()
```

**Atomic Trigger Write**:
```powershell
# Write trigger atomically (prevents partial-read race)
$content = @{ timestamp = (Get-Date).ToString("o") } | ConvertTo-Json
$content | Out-File "$SharedPath\$TriggerName.tmp" -Encoding UTF8
Rename-Item "$SharedPath\$TriggerName.tmp" $TriggerName
```

**Key Functions**:
- `Invoke-BuildAndDeploy`: Publishes (framework-dependent), copies to deploy path, writes trigger
- `FileSystemWatcher`: Monitors `.cs`, `.csproj`, etc. with noise filtering

**Build Command** (framework-dependent, not self-contained):
```powershell
dotnet publish $ProjectPath -c Release -o $tempBuildDir --nologo -v q
# No -r win-x64 --self-contained, uses pre-installed .NET
```

**Noise Filtering** (files to ignore):
- `\bin\`, `\obj\` - build artifacts
- `.git`, `.vs` - IDE/VCS folders
- `tmp`, `temp` - temporary files

#### 3.1.4 Combined Watcher Option

A single `watch-all.ps1` script could run both watchers in parallel:
```powershell
# Start both watchers as background jobs
Start-Job -ScriptBlock { & .\watch-dev.ps1 }
Start-Job -ScriptBlock { & .\watch-app.ps1 }
Wait-Job -Any
```

### 3.2 Sandbox-Side: bootstrap.ps1

**Responsibility**: Start server AND app, monitor for updates, handle graceful shutdown

**Parameters**:
| Parameter | Default | Description |
|-----------|---------|-------------|
| `$SharedFolder` | `C:\Shared` | Communication folder (sandbox path) |

**Paths**:
| Variable | Value | Purpose |
|----------|-------|---------|
| `$ServerSource` | `C:\Server` | Read-only mapped server binaries |
| `$AppSource` | `C:\App` | Read-only mapped app binaries |
| `$LocalServerDir` | `C:\LocalServer` | Writable server execution directory |
| `$LocalAppDir` | `C:\LocalApp` | Writable app execution directory |
| `$LogPath` | `C:\Shared\bootstrap.log` | Transcript output |

**Signal Files**:
| File | Direction | Purpose |
|------|-----------|---------|
| `mcp-ready.signal` | sandbox→host | Server started, includes PIDs and timestamp |
| `server.trigger` | host→sandbox | New server build, restart server only |
| `app.trigger` | host→sandbox | New app build, restart app only |
| `shutdown.signal` | host→sandbox | Graceful shutdown of both processes |

**Process Management with Job Object**:
```powershell
# Create Job Object - child processes auto-killed when bootstrap exits
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class JobObject {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
}
"@

$jobHandle = [JobObject]::CreateJobObject([IntPtr]::Zero, "SandboxDevJob")
# Configure KillOnJobClose (details in implementation)

$ServerProcess = $null  # Track PID for hot-reload
$AppProcess = $null     # Track PID for hot-reload
```

**Trigger Detection** (FileSystemWatcher + 20s fallback poll):
```powershell
# Primary: FileSystemWatcher for instant response
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $SharedFolder
$watcher.Filter = "*.trigger"
$watcher.NotifyFilter = [System.IO.NotifyFilters]::FileName
Register-ObjectEvent $watcher "Created" -Action { Handle-Trigger $Event.SourceEventArgs.Name }
$watcher.EnableRaisingEvents = $true

# Fallback: Poll every 20s in case FSW misses something
while (-not (Test-Path "$SharedFolder\shutdown.signal")) {
    Start-Sleep -Seconds 20
    Get-ChildItem "$SharedFolder\*.trigger" | ForEach-Object { Handle-Trigger $_.Name }
}
```

**Handle-Trigger Function**:
1. Check trigger type (`server.trigger` or `app.trigger`)
2. Delete trigger file
3. Stop specific process by tracked PID
4. Copy files from mapped folder to local execution folder
5. Start new process, assign to Job Object, track new PID
6. Log restart with timestamp

**Monitor Loop**:
1. FSW handles triggers instantly (no polling delay)
2. 20s fallback poll catches anything FSW missed
3. Check for `shutdown.signal` → exit loop (Job Object auto-kills children)
4. Check if server/app crashed → log warning, wait for next trigger

### 3.3 Sandbox Configuration: sandbox-dev.wsb

**Mapped Folders**:
| Host Path | Sandbox Path | Mode | Purpose |
|-----------|--------------|------|---------|
| `C:\TransportTest\Server` | `C:\Server` | read-only | MCP Server binaries |
| `C:\TransportTest\App` | `C:\App` | read-only | Test Application binaries |
| `C:\TransportTest\DotNet` | `C:\DotNet` | read-only | Pre-downloaded .NET runtime |
| `C:\TransportTest\Shared` | `C:\Shared` | read-write | Communication & triggers |

**LogonCommand**: Sets `DOTNET_ROOT` and `PATH` to include .NET, then runs bootstrap.

**Disabled Features**:
- `VGpu`: Disable (not needed for automation)
- `Networking`: Disable (security, not needed)

**WSB Configuration**:
```xml
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>C:\TransportTest\Server</HostFolder>
      <SandboxFolder>C:\Server</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\TransportTest\App</HostFolder>
      <SandboxFolder>C:\App</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\TransportTest\DotNet</HostFolder>
      <SandboxFolder>C:\DotNet</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\TransportTest\Shared</HostFolder>
      <SandboxFolder>C:\Shared</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell -ExecutionPolicy Bypass -Command "$env:DOTNET_ROOT = 'C:\DotNet'; $env:PATH = 'C:\DotNet;' + $env:PATH; C:\Server\bootstrap.ps1"</Command>
  </LogonCommand>
</Configuration>
```

### 3.4 Communication Protocol

**Request/Response** (JSON-RPC 2.0 over files):
```
Host writes:  C:\TransportTest\Shared\request-{id}.json
Server reads: C:\Shared\request-{id}.json
Server writes: C:\Shared\response-{id}.json
Host reads:   C:\TransportTest\Shared\response-{id}.json
```

**Health Check Request**:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "ping",
  "params": { "timestamp": "2024-01-15T10:30:00Z" }
}
```

**Ready Signal Content**:
```json
{
  "timestamp": "2024-01-15T10:30:00Z",
  "hostname": "SANDBOX-PC",
  "server_pid": 1234,
  "app_pid": 5678,
  "server_path": "C:\\LocalServer",
  "app_path": "C:\\LocalApp"
}
```

## 4. Data Models

### 4.1 Trigger File Format

**update.trigger**: Contains ISO 8601 timestamp
```
2024-01-15T10:30:00.0000000-08:00
```

**shutdown.signal**: Contains JSON with reason
```json
{
  "timestamp": "2024-01-15T10:30:00Z",
  "reason": "user-requested"
}
```

### 4.2 Build Metadata (Future Enhancement)

To support version verification, the build could embed metadata:
```json
{
  "build_timestamp": "2024-01-15T10:30:00Z",
  "git_commit": "abc1234",
  "git_dirty": true
}
```

## 5. Error Handling

### 5.1 Build Failures

```powershell
# watch-dev.ps1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    return  # Don't deploy, don't trigger, continue watching
}
```

### 5.2 File Copy Failures (Locked Files)

```powershell
# watch-dev.ps1
try {
    Copy-Item $file.FullName $destPath -Force -ErrorAction Stop
} catch {
    Write-Warning "Could not copy $relPath (File locked?)"
    # Continue with other files
}
```

### 5.3 Server Crash Recovery

```powershell
# bootstrap.ps1
if ($ServerProcess -ne $null -and $ServerProcess.HasExited) {
    # Don't auto-restart - wait for next update trigger
    # This prevents crash loops
}
```

### 5.4 Infrastructure Change Detection

Files that require sandbox restart:
- `bootstrap.ps1` changes → warn user
- `*.wsb` changes → warn user

```powershell
# watch-dev.ps1 enhancement
$infrastructureFiles = @("bootstrap.ps1", "*.wsb")
if ($changedFile -match $infrastructureFiles) {
    Write-Warning "Infrastructure file changed! Sandbox restart required."
}
```

## 6. Testing Strategy

### 6.1 Unit Tests (Host-Side)

Not practical for PowerShell file watcher scripts. Focus on integration testing.

### 6.2 Integration Tests

**Test: Hot-Reload Cycle**
1. Launch sandbox with test-phase1.ps1 -Setup, then -LaunchOnly
2. Start watch-dev.ps1 in parallel
3. Make a code change (e.g., add log statement)
4. Verify: build triggered, files deployed, server restarted
5. Send ping request, verify response

**Test: Build Failure Recovery**
1. Introduce syntax error in code
2. Verify build fails, no trigger written
3. Fix syntax error
4. Verify next build succeeds and deploys

**Test: Graceful Shutdown**
1. Write shutdown.signal
2. Verify bootstrap exits cleanly
3. Verify server process terminated

### 6.3 Manual Testing Checklist

- [ ] Sandbox launches and signals ready within 60s
- [ ] Code change triggers build within 2s debounce
- [ ] Build + deploy + restart completes in <10s
- [ ] Multiple rapid changes only trigger one build
- [ ] Build failure doesn't crash watcher
- [ ] Locked file warning doesn't crash deploy
- [ ] Shutdown signal triggers clean exit

## 7. File Structure

```
winforms-mcp/
├── src/
│   ├── Rhombus.WinFormsMcp.Server/
│   │   └── ...                    # MCP Server source
│   └── Rhombus.WinFormsMcp.TestApp/
│       └── ...                    # Test Application source
├── sandbox/
│   ├── bootstrap.ps1              # Sandbox-side monitor (manages both processes)
│   ├── watch-dev.ps1              # Host-side watcher (MCP Server)
│   ├── watch-app.ps1              # Host-side watcher (Test Application)
│   ├── watch-all.ps1              # Combined watcher (optional)
│   ├── sandbox-dev.wsb            # Sandbox configuration
│   ├── setup.ps1                  # Initial setup script
│   └── test.ps1                   # Integration test script
└── docs/
    └── SANDBOX_DEVELOPMENT.md     # User documentation

Host File System (C:\TransportTest\):
├── Server/                        # MCP Server binaries (mapped read-only)
│   ├── Rhombus.WinFormsMcp.Server.exe
│   ├── *.dll
│   └── bootstrap.ps1
├── App/                           # Test App binaries (mapped read-only)
│   ├── Rhombus.WinFormsMcp.TestApp.exe
│   └── *.dll
├── DotNet/                        # Pre-downloaded .NET runtime (mapped read-only)
│   ├── dotnet.exe
│   └── shared/                    # Runtime libraries
└── Shared/                        # Communication (mapped read-write)
    ├── mcp-ready.signal
    ├── server.trigger
    ├── app.trigger
    ├── shutdown.signal
    ├── request-*.json
    ├── response-*.json
    └── bootstrap.log
```
