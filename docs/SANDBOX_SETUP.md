# Windows Sandbox Setup Guide

A guide for configuring Windows Sandbox for isolated UI automation testing.

## Prerequisites

### Windows Requirements

- **Windows 10 Pro/Enterprise** or **Windows 11 Pro/Enterprise**
- Windows Sandbox feature enabled
- Hyper-V enabled (required by Windows Sandbox)

### Enable Windows Sandbox

```powershell
# Check if virtualization is supported
systeminfo | Select-String "Hyper-V"

# Enable Windows Sandbox (requires reboot)
Enable-WindowsOptionalFeature -FeatureName "Containers-DisposableClientVM" -Online -NoRestart
```

---

## Configuration Format

### .wsb File Structure

Windows Sandbox configurations use XML format with `.wsb` extension:

```xml
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>C:\TestApps\MyApp</HostFolder>
      <SandboxFolder>C:\App</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\MCPServer</HostFolder>
      <SandboxFolder>C:\MCP</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\AgentOutput</HostFolder>
      <SandboxFolder>C:\Output</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell -ExecutionPolicy Bypass -File C:\MCP\bootstrap.ps1</Command>
  </LogonCommand>
</Configuration>
```

### Configuration Options

| Element | Description |
|---------|-------------|
| VGpu | Enable/Disable GPU virtualization. Disable for automation. |
| Networking | Enable/Disable network access. Disable for security. |
| MappedFolders | Host folders visible inside sandbox |
| LogonCommand | Command to run on sandbox startup |

---

## Folder Mapping

### Mapping Concepts

- **HostFolder**: Path on your Windows machine
- **SandboxFolder**: Path inside the sandbox (always C:\...)
- **ReadOnly**: true = sandbox cannot modify files

### Standard Mapping Layout

```
Host                            Sandbox
----                            -------
C:\MCPServer\                   C:\MCP\          (read-only)
  ├─ Rhombus.WinFormsMcp.Server.exe
  ├─ bootstrap.ps1
  └─ vdd\ (virtual display driver)

C:\TestApps\MyApp\              C:\App\          (read-only)
  └─ MyApp.exe

C:\AgentOutput\                 C:\Output\       (read-write)
  ├─ screenshots\
  ├─ logs\
  └─ results\
```

---

## Security Considerations

### Path Validation

The MCP server validates all paths before creating .wsb configurations:

**Blocked Paths** (will be rejected):
- `C:\Users\{username}\Documents`
- `C:\Users\{username}\Desktop`
- `C:\Users\{username}\AppData`
- `C:\Windows`
- `C:\Program Files`
- `C:\Program Files (x86)`

**Why**: These paths contain sensitive user data that should never be exposed to automated testing.

### Symlink Resolution

All paths are resolved to canonical form before validation:

```
C:\Link -> C:\Users\jhedin\Documents
```

If `C:\Link` is mapped, the MCP server resolves the symlink and rejects it.

### Path Traversal Prevention

Paths with `..` are normalized:

```
C:\TestApps\..\..\..\Users\jhedin\Documents
```

Becomes `C:\Users\jhedin\Documents` and is rejected.

### Case-Insensitive Validation

All paths are compared case-insensitively:

```
C:\USERS\JHEDIN\DOCUMENTS  →  Rejected
c:\users\jhedin\documents  →  Rejected
```

---

## Bootstrap Script

### bootstrap.ps1 Template

```powershell
# bootstrap.ps1 - Runs inside sandbox on startup

# 1. Wait for filesystem to settle
Start-Sleep -Seconds 2

# 2. Create output directories
New-Item -ItemType Directory -Path "C:\Output\screenshots" -Force | Out-Null
New-Item -ItemType Directory -Path "C:\Output\logs" -Force | Out-Null

# 3. Signal that bootstrap is starting
"bootstrap_started" | Out-File "C:\Output\bootstrap.signal"

# 4. Launch the target application
Start-Process -FilePath "C:\App\MyApp.exe" -WorkingDirectory "C:\App"

# 5. Wait for app to initialize
Start-Sleep -Seconds 3

# 6. Launch MCP server
$mcpProcess = Start-Process -FilePath "C:\MCP\Rhombus.WinFormsMcp.Server.exe" `
    -ArgumentList "--shared-folder C:\Output" `
    -WorkingDirectory "C:\MCP" `
    -PassThru

# 7. Signal ready
@{
    status = "ready"
    mcp_pid = $mcpProcess.Id
    timestamp = (Get-Date -Format o)
} | ConvertTo-Json | Out-File "C:\Output\ready.signal"

# 8. Keep running (sandbox closes when this script exits)
while ($true) {
    Start-Sleep -Seconds 60
}
```

### Ready Signal Format

The host waits for `ready.signal` to appear in the shared folder:

```json
{
  "status": "ready",
  "mcp_pid": 1234,
  "timestamp": "2026-01-19T10:30:00Z"
}
```

---

## Launching the Sandbox

### From PowerShell

```powershell
# Generate .wsb configuration
$wsbPath = "C:\Temp\test-session.wsb"
# ... (MCP server generates this)

# Launch sandbox (requires admin for first launch)
Start-Process -FilePath "WindowsSandbox.exe" -ArgumentList $wsbPath
```

### Via MCP Tool

```json
{
  "tool": "launch_app_sandboxed",
  "args": {
    "appPath": "C:\\TestApps\\MyApp\\MyApp.exe",
    "mcpServerPath": "C:\\MCPServer",
    "outputFolder": "C:\\AgentOutput"
  }
}
```

---

## Communication

### Shared Folder Polling

Host and sandbox communicate via files in the shared folder:

```
C:\AgentOutput\               (host sees this)
C:\Output\                    (sandbox sees this)
  ├─ request.json             Host writes, sandbox reads
  ├─ response.json            Sandbox writes, host reads
  ├─ ready.signal             Sandbox writes when ready
  └─ shutdown.signal          Host writes to request shutdown
```

**Typical latency**: ~50-100ms per request/response cycle.

### Request Format

```json
{
  "id": "req-001",
  "method": "get_ui_tree",
  "params": { "maxDepth": 3 }
}
```

### Response Format

```json
{
  "id": "req-001",
  "result": { "tree": "...", "element_count": 42 }
}
```

---

## Shutdown

### Graceful Shutdown

```powershell
# Host writes shutdown signal
"shutdown" | Out-File "C:\AgentOutput\shutdown.signal"

# Wait for sandbox to close (timeout 30 seconds)
$timeout = 30
$elapsed = 0
while ((Get-Process -Name "WindowsSandbox*" -ErrorAction SilentlyContinue) -and ($elapsed -lt $timeout)) {
    Start-Sleep -Seconds 1
    $elapsed++
}
```

### Force Shutdown

Avoid `Stop-Process` on the sandbox process as it can cause issues. Instead:

```powershell
# Close sandbox window gracefully
$sandbox = Get-Process -Name "WindowsSandboxClient" -ErrorAction SilentlyContinue
if ($sandbox) {
    $sandbox.CloseMainWindow()
    $sandbox.WaitForExit(10000)
}
```

---

## Troubleshooting

### Sandbox Won't Start

**Symptom**: WindowsSandbox.exe crashes immediately

**Solutions**:
1. Run from admin PowerShell:
   ```powershell
   Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'WindowsSandbox.exe config.wsb'
   ```
2. Check Hyper-V is enabled
3. Verify Windows Sandbox feature is enabled

### Files Not Visible in Sandbox

**Symptom**: Mapped folders appear empty

**Solutions**:
1. Unblock files from internet zone:
   ```powershell
   Get-ChildItem -Path "C:\TestApps" -Recurse | Unblock-File
   ```
2. Check folder permissions on host
3. Verify paths don't contain special characters

### MCP Server Fails to Start

**Symptom**: ready.signal never appears

**Solutions**:
1. Check if .NET runtime is available (use self-contained publish)
2. Verify MCP server path is correct in .wsb
3. Check bootstrap.ps1 for errors:
   ```powershell
   # In bootstrap.ps1, add logging
   $ErrorActionPreference = "Stop"
   Start-Transcript -Path "C:\Output\bootstrap.log"
   ```

### Touch Input Permission Denied

**Symptom**: `InjectTouchInput` fails with access denied

**Solution**: This should work automatically in sandbox (runs as Admin). If not:
1. Verify sandbox has Admin privileges (default)
2. Check if virtualization-based security is blocking

### Network Requests Fail

**Symptom**: Web requests timeout or fail

**This is expected**: Network is disabled by default for security.

If you need network (not recommended):
```xml
<Networking>Enable</Networking>
```

---

## Best Practices

### Security

1. **Always disable networking** unless absolutely required
2. **Map folders read-only** when possible
3. **Never map user profile folders** (Documents, Desktop, etc.)
4. **Validate all paths** before generating .wsb files

### Performance

1. **Use minimal folder mappings** - each mapping adds overhead
2. **Disable VGpu** for automation workloads
3. **Pre-install dependencies** in the MCP server publish folder
4. **Use self-contained publish** to avoid .NET runtime issues

### Reliability

1. **Wait for ready signal** before sending MCP requests
2. **Use graceful shutdown** instead of killing processes
3. **Handle sandbox crash** by detecting stale ready.signal
4. **Implement request timeout** for hung sandbox sessions
