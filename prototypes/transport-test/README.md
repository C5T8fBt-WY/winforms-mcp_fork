# MCP Transport Layer Prototype

Tests two transport mechanisms for host ↔ Windows Sandbox MCP communication.

## Status

| Transport | Code | Build | Local Test | Sandbox Test |
|-----------|------|-------|------------|--------------|
| Named Pipe | ✅ | ✅ | ✅ | ❌ **FAILED** (as expected) |
| Shared Folder | ✅ | ✅ | ✅ | ✅ **PASSED** |

**Result**: Named pipes do NOT work across sandbox VM boundary. Shared folder polling is the confirmed transport mechanism.

### Named Pipe Failure Details (2026-01-13)
```
Attempting connection to server: '.' ... FAILED: TimeoutException
Attempting connection to server: 'localhost' ... FAILED: IOException (network name not available)
Attempting connection to server: 'MACHINE-NAME' ... FAILED: IOException (network name not available)

CONCLUSION: Named pipes are local to the VM's namespace, not bridged to host.
```

## Prerequisites

- **Windows 11 Pro** (or Enterprise/Education) - Home edition does NOT have Windows Sandbox
- **.NET 8.0 SDK** - for building the projects

### Enable Windows Sandbox (after upgrading to Pro)

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
# From WSL
powershell.exe -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Start-Process WindowsSandbox.exe; exit'"

# Or with a specific .wsb file:
powershell.exe -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'Start-Process WindowsSandbox.exe -ArgumentList ''C:\path\to\config.wsb''; exit'"
```

**From an already-admin PowerShell:**
```powershell
# Just run directly
WindowsSandbox.exe "C:\path\to\config.wsb"
```

**Tip**: Create a desktop shortcut or shell alias if you use this frequently.

---

## Option 1: Named Pipe Transport

**Hypothesis**: Named pipes will likely NOT work across the sandbox boundary because Windows Sandbox runs as an isolated Hyper-V VM with its own pipe namespace.

### Files

- `NamedPipeHostServer/` - Host-side server (creates `\\.\pipe\mcp-sandbox-test`)
- `NamedPipeClientTest/` - Sandbox-side client (tries `.`, `localhost`, machine name)
- `test-named-pipe.wsb` - Sandbox configuration

### Test Procedure

**Setup (run once in admin PowerShell):**
```powershell
# Create output folder and copy wsb config
New-Item -ItemType Directory -Force -Path "C:\TransportTest\Output"
Copy-Item "\\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\prototypes\transport-test\test-named-pipe.wsb" "C:\TransportTest\"

# Then run "Build Commands" section above to publish self-contained binaries
```

**Terminal 1 - Host server (admin PowerShell):**
```powershell
C:\TransportTest\PipeHost\NamedPipeHostServer.exe
```

**Terminal 2 - Launch sandbox (admin PowerShell):**
```powershell
# Must be from admin PowerShell due to coreclr.dll bug in Sandbox 0.5.3.0
WindowsSandbox.exe C:\TransportTest\test-named-pipe.wsb
```

### Expected Result

Named pipes will likely fail. Client will report:
```json
{
  "connection_successful": false,
  "conclusion": "Named pipes do NOT work across Windows Sandbox VM boundary",
  "recommendation": "Use shared folder polling transport instead"
}
```

---

## Option 2: Shared Folder Polling Transport (Fallback)

File-based request/response using mapped folders. Guaranteed to work since folder mapping is a core Windows Sandbox feature.

### Files

- `SharedFolderHost/` - Host-side (writes request JSON, polls for response)
- `SharedFolderClient/` - Sandbox-side (polls for requests, writes responses)
- `test-shared-folder.wsb` - Sandbox configuration

### Protocol

```
Host                          Sandbox
  |                              |
  |-- request-{id}.json -------->|
  |                              |-- processes request
  |<----- response-{id}.json ----|
  |-- deletes both files         |
```

### Test Procedure

**Setup (run once in admin PowerShell):**
```powershell
# Create communication folder and copy wsb config
New-Item -ItemType Directory -Force -Path "C:\TransportTest\Shared"
Copy-Item "\\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\prototypes\transport-test\test-shared-folder.wsb" "C:\TransportTest\"

# Then run "Build Commands" section above to publish self-contained binaries
```

**Terminal 1 - Host (admin PowerShell):**
```powershell
C:\TransportTest\Host\SharedFolderHost.exe C:\TransportTest\Shared
```

**Terminal 2 - Launch sandbox (admin PowerShell):**
```powershell
# Must be from admin PowerShell due to coreclr.dll bug in Sandbox 0.5.3.0
WindowsSandbox.exe C:\TransportTest\test-shared-folder.wsb
```

### Expected Result

Shared folder transport will work with ~50-200ms latency per request.

```json
{
  "test": "shared_folder_transport",
  "connection_successful": true,
  "latency_ms": {
    "p50": 25,
    "p95": 80,
    "max": 150
  },
  "pass": true
}
```

---

## Local Testing (Without Sandbox)

You can test the protocol locally (both apps on same machine) to verify code works:

**Terminal 1:**
```powershell
.\SharedFolderClient.exe C:\TransportTest\Shared
```

**Terminal 2:**
```powershell
.\SharedFolderHost.exe C:\TransportTest\Shared
```

**Note**: Windows SmartScreen may block execution. Either:
- Click "More info" → "Run anyway"
- Or unblock files: `Get-ChildItem *.exe -Recurse | Unblock-File`

---

## Latency Targets

| Transport | Target P95 | Expected |
|-----------|------------|----------|
| Named Pipe | <100ms | ~5ms (if works) |
| Shared Folder | <500ms | ~50-200ms |

Shared folder polling is acceptable for MCP tool calls since each call typically takes 500ms+ for actual UI automation work.

---

## Build Commands

**IMPORTANT**: Windows Sandbox does not have .NET runtime installed. You must publish as **self-contained** for sandbox clients.

```powershell
cd \\wsl.localhost\Ubuntu\home\jhedin\workspace\magpie-craft\winforms-mcp\prototypes\transport-test

# Publish self-contained for sandbox (includes .NET runtime, ~180 files)
dotnet publish SharedFolderClient -c Release -r win-x64 --self-contained true -o C:\TransportTest\SharedClient
dotnet publish NamedPipeClientTest -c Release -r win-x64 --self-contained true -o C:\TransportTest\Client

# Publish self-contained for host (optional, but consistent)
dotnet publish SharedFolderHost -c Release -r win-x64 --self-contained true -o C:\TransportTest\Host
dotnet publish NamedPipeHostServer -c Release -r win-x64 --self-contained true -o C:\TransportTest\PipeHost

# Unblock all files (removes "from internet" flag)
Get-ChildItem C:\TransportTest -Recurse | Unblock-File
```

---

## Decision

After testing, update `design.md` Section 8.1 with:

- If named pipes work: Use named pipes (lower latency)
- If named pipes fail: Use shared folder polling (guaranteed to work)

The shared folder approach is the safe fallback and will definitely work.
