# Sandbox Development Workflow

Guide for developing and testing the MCP server inside Windows Sandbox with hot-reload support.

## Prerequisites

- **Windows 11 Pro** (or Enterprise/Education) - Home edition does NOT have Windows Sandbox
- **.NET 8.0 SDK** - for building the projects

### Enable Windows Sandbox

```powershell
# Run as Administrator
Enable-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" -All
# Reboot required
```

### Known Issue: coreclr.dll Crash (Windows Sandbox 0.5.3.0)

Windows Sandbox 0.5.3.0 has a known bug that causes coreclr.dll crashes when launched from a non-admin terminal. The sandbox window opens but immediately crashes.

**Workaround**: Launch Windows Sandbox from an **admin PowerShell session**.

**Quick launch from WSL or non-admin terminal:**
```bash
# From WSL - elevates to admin and launches sandbox
powershell.exe -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Start-Process ''C:\TransportTest\sandbox-dev.wsb''; exit'"
```

**From an already-admin PowerShell:**
```powershell
# Just run directly
C:\TransportTest\sandbox-dev.wsb
# Or: WindowsSandbox.exe C:\TransportTest\sandbox-dev.wsb
```

## Quick Start

### 1. Initial Setup (One Time)

Run from **admin PowerShell** on Windows:

```powershell
cd \\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\sandbox
.\setup.ps1
```

This creates:
- `C:\TransportTest\Server\` - MCP server binaries (mapped read-only into sandbox)
- `C:\TransportTest\App\` - Test app binaries (mapped read-only into sandbox)
- `C:\TransportTest\DotNet\` - .NET 8 runtime (mapped read-only into sandbox)
- `C:\TransportTest\Shared\` - Communication folder (mapped read-write)
- `C:\TransportTest\sandbox-dev.wsb` - Sandbox configuration

### 2. Launch Sandbox and Watchers

**Terminal 1 - Start the watchers:**
```powershell
cd \\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\sandbox
.\watch-all.ps1
```

Or run them individually:
```powershell
.\watch-dev.ps1    # MCP server watcher
.\watch-app.ps1    # Test app watcher (in another terminal)
```

**Terminal 2 - Launch sandbox (must be admin PowerShell):**
```powershell
# From admin PowerShell
C:\TransportTest\sandbox-dev.wsb

# Or from WSL/non-admin terminal (elevates automatically)
powershell.exe -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Start-Process ''C:\TransportTest\sandbox-dev.wsb''; exit'"
```

**Note**: Due to a coreclr.dll bug in Windows Sandbox 0.5.3.0, the sandbox must be launched from an admin PowerShell session. See Prerequisites section above.

### 3. Development Workflow

1. Make code changes in your editor
2. The watcher detects changes and auto-builds (100ms debounce)
3. The sandbox receives a trigger and hot-reloads the process (~2-3s)
4. No sandbox restart needed!

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│ Host (Windows)                                                       │
│                                                                      │
│  ┌─────────────────┐      ┌───────────────────────────────────────┐ │
│  │ watch-dev.ps1   │      │ C:\TransportTest\                     │ │
│  │ watch-app.ps1   │      │ ├── Server\ (server binaries)         │ │
│  │ (100ms debounce │─────▶│ ├── App\ (test app binaries)          │ │
│  │  + build lock)  │      │ ├── DotNet\ (.NET runtime)            │ │
│  └─────────────────┘      │ └── Shared\ (triggers, signals)       │ │
│                           └───────────────────────────────────────┘ │
│                                           │ (mapped folders)        │
│                                           ▼                         │
│                           ┌───────────────────────────────────────┐ │
│                           │ Windows Sandbox                       │ │
│                           │                                       │ │
│                           │  C:\Server\ ──────▶ C:\LocalServer\   │ │
│                           │  C:\App\ ─────────▶ C:\LocalApp\      │ │
│                           │  C:\DotNet\ ─────▶ (in PATH)          │ │
│                           │  C:\Shared\ ──────▶ triggers/signals  │ │
│                           │                                       │ │
│                           │  bootstrap.ps1 (Job Object manager):  │ │
│                           │  - Monitors *.trigger files (FSW)     │ │
│                           │  - Hot-reloads processes by PID       │ │
│                           │  - Auto-kills children on exit        │ │
│                           └───────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

## Scripts Reference

| Script | Purpose |
|--------|---------|
| `setup.ps1` | One-time setup (creates folders, downloads .NET, initial build) |
| `setup-dotnet.ps1` | Download .NET 8 runtime to `C:\TransportTest\DotNet` |
| `watch-dev.ps1` | Watch MCP server code, build, deploy, trigger |
| `watch-app.ps1` | Watch test app code, build, deploy, trigger |
| `watch-all.ps1` | Run both watchers as background jobs |
| `test.ps1` | Integration test (launch, ping, hot-reload, shutdown) |
| `sandbox-dev.wsb` | Sandbox configuration file |
| `bootstrap.ps1` | Runs inside sandbox (copied to Server folder) |

## Signal Files

| File | Purpose |
|------|---------|
| `mcp-ready.signal` | Bootstrap writes after startup (JSON with PIDs, paths) |
| `shutdown.signal` | Host writes to request graceful shutdown |
| `server.trigger` | Host writes to trigger MCP server hot-reload |
| `app.trigger` | Host writes to trigger test app hot-reload |
| `bootstrap.log` | Transcript log from inside sandbox |

## When Sandbox Restart is Required

Hot-reload works for most code changes. Restart sandbox when you modify:

- `bootstrap.ps1` - The script running inside sandbox
- `sandbox-dev.wsb` - Sandbox configuration (mapped folders, LogonCommand)
- `setup-dotnet.ps1` - .NET runtime setup

The watch script will warn you when these files change:

```
========================================
INFRASTRUCTURE CHANGE DETECTED!
File: C:\...\sandbox\bootstrap.ps1 (Changed)

Sandbox restart required for this change to take effect.
Close sandbox and run it again.
========================================
```

## Debugging & Logs

### Log Locations

| Location | Description |
|----------|-------------|
| `C:\TransportTest\Shared\bootstrap.log` | Sandbox-side: process starts/stops, trigger handling, crashes |
| Watch script console output | Host-side: build results, deploy status, trigger writes |
| `C:\TransportTest\Shared\mcp-ready.signal` | Current PIDs, TCP endpoint, timestamps |

### Checking Sandbox Logs

```powershell
# Full log
Get-Content C:\TransportTest\Shared\bootstrap.log

# Last 50 lines (recent activity)
Get-Content C:\TransportTest\Shared\bootstrap.log -Tail 50

# Follow in real-time
Get-Content C:\TransportTest\Shared\bootstrap.log -Wait -Tail 20
```

### Checking Current State

```powershell
# Current PIDs and TCP info
Get-Content C:\TransportTest\Shared\mcp-ready.signal | ConvertFrom-Json

# Example output:
# server_pid : 1348
# tcp_ip     : 172.29.16.229
# tcp_port   : 9999
# app_pid    : 4980
```

### What the Logs Show

**Server hot-reload example:**
```
Poll: Server trigger found
[18:36:09] Handling trigger: server.trigger
Stopping server (PID: 7036)...
[18:36:10] Server restarted (PID: 1680)
Ready signal updated (Server: 1680, App: )
TCP endpoint: 172.29.16.229:9999
```

**App crash detection:**
```
[18:40:15] App crashed! Exit code: -1 (PID was: 4980)
```

## App Management via MCP

### Automatic Previous Instance Cleanup

The `launch_app` tool automatically closes any previous instance of the same executable before launching a new one. This prevents accumulating multiple app instances.

**Example response when launching over an existing instance:**
```json
{
  "success": true,
  "result": {
    "pid": 4980,
    "processName": "Rhombus.WinFormsMcp.TestApp",
    "previousPid": 7092,
    "previousClosed": true
  }
}
```

- `previousPid`: The PID that was closed
- `previousClosed`: Whether the close was successful

**Behavior:**
1. Checks if the same executable was previously launched via `launch_app`
2. If running, attempts graceful close via `CloseMainWindow()`
3. Waits up to 2 seconds for graceful exit
4. Falls back to `Kill()` if graceful close fails
5. Launches the new instance

### Manual App Control

```powershell
# Close a specific app by PID
$request = @{
    jsonrpc = "2.0"; id = 1
    method = "tools/call"
    params = @{ name = "close_app"; arguments = @{ pid = 4980 } }
} | ConvertTo-Json -Depth 5

# Launch app (auto-closes previous)
$request = @{
    jsonrpc = "2.0"; id = 1
    method = "tools/call"
    params = @{ name = "launch_app"; arguments = @{ path = "C:\App\MyApp.exe" } }
} | ConvertTo-Json -Depth 5
```

## Troubleshooting

### Check Bootstrap Log

```powershell
Get-Content C:\TransportTest\Shared\bootstrap.log -Tail 50
```

### Sandbox Won't Start

1. Ensure Windows Sandbox feature is enabled:
   ```powershell
   Enable-WindowsOptionalFeature -FeatureName "Containers-DisposableClientVM" -Online
   ```

2. Restart may be required after enabling

### Server Not Starting

Check that binaries were properly published:
```powershell
dir C:\TransportTest\Server\*.exe
```

Should show `Rhombus.WinFormsMcp.Server.exe`.

### Hot-Reload Not Working

1. Ensure watchers are running (`watch-all.ps1` or individual watchers)
2. Check that `C:\TransportTest\Shared\server.trigger` is being created
3. Check bootstrap.log for errors during restart
4. FileSystemWatcher might miss events - wait 20s for fallback poll

### .NET Not Found in Sandbox

The sandbox uses a pre-downloaded .NET runtime. If missing:
```powershell
.\setup-dotnet.ps1 -Force
```

Then restart the sandbox.

## Development Tips

### Avoid Sandbox Restarts

The sandbox takes ~10-20 seconds to boot. Use hot-reload instead:
- Keep sandbox running
- Use watchers to auto-deploy changes
- Server restarts in ~2-3 seconds vs full sandbox reboot

### Run Integration Test

```powershell
.\test.ps1 -TestHotReload -KeepAlive
```

### Testing Specific Tools

Use the shared folder to send manual requests:

```powershell
$request = @{
    jsonrpc = "2.0"
    id = 1
    method = "get_ui_tree"
    params = @{ maxDepth = 2 }
} | ConvertTo-Json

$request | Out-File "C:\TransportTest\Shared\request-1.json"

# Wait for response
while (!(Test-Path "C:\TransportTest\Shared\response-1.json")) {
    Start-Sleep -Milliseconds 100
}

Get-Content "C:\TransportTest\Shared\response-1.json"
```

### Clean Shutdown

```powershell
"shutdown" | Out-File "C:\TransportTest\Shared\shutdown.signal"
```

Or just close the sandbox window.

## Key Design Decisions

1. **Pre-downloaded .NET Runtime** - Downloaded once to host, mapped read-only into sandbox. Enables framework-dependent builds (~5MB vs 80MB self-contained).

2. **100ms Debounce + Build Lock** - Watchers debounce rapid file changes and queue builds if one is in progress. Build time is the natural throttle.

3. **Windows Job Object** - Bootstrap uses Job Object with KillOnJobClose flag. When bootstrap exits (or sandbox closes), all child processes are automatically killed. No orphan processes.

4. **FileSystemWatcher + 20s Fallback Poll** - FSW provides instant trigger detection. Fallback poll catches any missed events.

5. **Atomic Trigger Files** - Triggers written to `.tmp` then renamed. Prevents partial-read race conditions.

6. **PID Tracking** - Bootstrap tracks PIDs of started processes for precise hot-reload control.
