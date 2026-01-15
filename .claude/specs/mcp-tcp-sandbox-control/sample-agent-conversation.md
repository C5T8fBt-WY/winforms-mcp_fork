# Sample Agent Conversation

This document shows a complete agent workflow: launching an app, discovering UI, interacting, and capturing results.

## Scenario

User asks: "Test the calculator app - open it, enter 5+3, click equals, and screenshot the result"

---

## Step 1: Agent Checks Sandbox Status

Agent reads the signal file to see if sandbox is ready:

```
Agent reads: C:\TransportTest\Shared\mcp-ready.signal
```

```json
{
  "tcp_ip": "172.23.144.1",
  "tcp_port": 9999,
  "server_pid": 1234,
  "ready": true
}
```

Agent connects via TCP to `172.23.144.1:9999`.

---

## Step 2: MCP Handshake

**Agent → Server:**
```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"claude-code","version":"1.0.0"}}}
```

**Server → Agent:**
```json
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","capabilities":{"tools":{}},"serverInfo":{"name":"winforms-mcp","version":"1.0.3"}}}
```

**Agent → Server (notification, no response expected):**
```json
{"jsonrpc":"2.0","method":"notifications/initialized"}
```

---

## Step 3: Launch the Application

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": {
      "path": "C:\\App\\Calculator.exe"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"pid\":5678,\"processName\":\"Calculator\",\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Agent now knows:**
- App PID: 5678
- Window handle: `0x1A2B3C`
- Window title: "Calculator"
- Window bounds: 320x480 at (100, 100)

---

## Step 4: Discover UI Elements

Agent wants to see what buttons are available:

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "list_elements",
    "arguments": {
      "windowHandle": "0x1A2B3C",
      "maxDepth": 3
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"elementCount\":25,\"elements\":[{\"automationId\":\"DisplayResult\",\"name\":\"0\",\"controlType\":\"Text\",\"bounds\":{\"x\":10,\"y\":10,\"width\":300,\"height\":60}},{\"automationId\":\"Num5\",\"name\":\"5\",\"controlType\":\"Button\",\"bounds\":{\"x\":70,\"y\":200,\"width\":60,\"height\":60}},{\"automationId\":\"Plus\",\"name\":\"+\",\"controlType\":\"Button\",\"bounds\":{\"x\":250,\"y\":140,\"width\":60,\"height\":60}},{\"automationId\":\"Num3\",\"name\":\"3\",\"controlType\":\"Button\",\"bounds\":{\"x\":70,\"y\":260,\"width\":60,\"height\":60}},{\"automationId\":\"Equals\",\"name\":\"=\",\"controlType\":\"Button\",\"bounds\":{\"x\":250,\"y\":320,\"width\":60,\"height\":120}}],\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Agent now knows the button AutomationIds:**
- `Num5` - the "5" button
- `Plus` - the "+" button
- `Num3` - the "3" button
- `Equals` - the "=" button
- `DisplayResult` - shows the result

---

## Step 5: Click "5"

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tools/call",
  "params": {
    "name": "click_by_automation_id",
    "arguments": {
      "automationId": "Num5",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

---

## Step 6: Click "+"

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "click_by_automation_id",
    "arguments": {
      "automationId": "Plus",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

---

## Step 7: Click "3"

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "tools/call",
  "params": {
    "name": "click_by_automation_id",
    "arguments": {
      "automationId": "Num3",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

---

## Step 8: Click "="

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "tools/call",
  "params": {
    "name": "click_by_automation_id",
    "arguments": {
      "automationId": "Equals",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

---

## Step 9: Verify the Result

Agent checks the display to confirm "8" is shown:

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "tools/call",
  "params": {
    "name": "check_element_state",
    "arguments": {
      "automationId": "DisplayResult",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"isEnabled\":true,\"isVisible\":true,\"value\":\"8\",\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Agent confirms:** Display shows "8" ✓

---

## Step 10: Take Screenshot

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 9,
  "method": "tools/call",
  "params": {
    "name": "take_screenshot",
    "arguments": {
      "outputPath": "C:\\Shared\\calculator-result.png",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 9,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"path\":\"C:\\\\Shared\\\\calculator-result.png\",\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Agent reads screenshot from host:** `C:\TransportTest\Shared\calculator-result.png`

---

## Step 11: Close Application (Optional)

**Agent → Server:**
```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "close_app",
    "arguments": {
      "pid": 5678
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[]}"
    }]
  }
}
```

**Agent notes:** No windows remaining (app closed successfully).

---

## Summary

| Step | Tool | Purpose |
|------|------|---------|
| 1 | (read signal file) | Discover sandbox TCP endpoint |
| 2 | initialize | MCP handshake |
| 3 | launch_app | Start the calculator |
| 4 | list_elements | Discover UI structure |
| 5-8 | click_by_automation_id | Click 5, +, 3, = |
| 9 | check_element_state | Verify result is "8" |
| 10 | take_screenshot | Capture final state |
| 11 | close_app | Clean up |

**Key observations:**
- Every response includes `windows` array so agent always knows current state
- Agent uses `windowHandle` for precise targeting after first discovery
- Agent uses `automationId` for reliable element targeting
- Screenshot saved to shared folder, accessible from host

---

## Hot Reload Workflow: Finding and Fixing a Bug

This scenario shows the full development loop: agent finds a bug, developer fixes the app code, agent verifies the fix.

### Scenario

User asks: "Test the calculator multiplication - verify 6 × 7 = 42"

### Phase 1: Agent Discovers the Bug

Agent launches the app and tests multiplication:

**Agent → Server (launch app):**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": { "path": "C:\\App\\Calculator.exe" }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"pid\":5678,\"processName\":\"Calculator\",\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

Agent clicks: 6, ×, 7, =

**Agent → Server (check result):**
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "tools/call",
  "params": {
    "name": "check_element_state",
    "arguments": {
      "automationId": "DisplayResult",
      "windowHandle": "0x1A2B3C"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"isEnabled\":true,\"isVisible\":true,\"value\":\"13\",\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Agent reports to user:**
> Bug found! 6 × 7 = 13 (expected 42). The multiplication operator appears to be performing addition instead.

---

### Phase 2: Developer Fixes the Code

The developer (or agent) fixes the bug in the source code:

```csharp
// Before (bug):
case "*": result = a + b; break;

// After (fix):
case "*": result = a * b; break;
```

Developer rebuilds the app:
```powershell
dotnet build -c Release -o C:\TransportTest\App\
```

---

### Phase 3: Agent Triggers Hot Reload

The agent needs to restart the MCP server to pick up the new app binaries. Since the app binaries are in a mapped folder, the agent just needs to:

1. Close the current app
2. Re-launch with the updated binaries

**Agent → Server (close current app):**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "tools/call",
  "params": {
    "name": "close_app",
    "arguments": { "pid": 5678 }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[]}"
    }]
  }
}
```

**Agent → Server (launch updated app):**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": { "path": "C:\\App\\Calculator.exe" }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"pid\":6789,\"processName\":\"Calculator\",\"windows\":[{\"handle\":\"0x2B3C4D\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Note:** New PID (6789) and new window handle (0x2B3C4D).

---

### Phase 4: Agent Verifies the Fix

Agent repeats the test with the fixed app:

Agent clicks: 6, ×, 7, = (using new window handle `0x2B3C4D`)

**Agent → Server (check result):**
```json
{
  "jsonrpc": "2.0",
  "id": 13,
  "method": "tools/call",
  "params": {
    "name": "check_element_state",
    "arguments": {
      "automationId": "DisplayResult",
      "windowHandle": "0x2B3C4D"
    }
  }
}
```

**Server → Agent:**
```json
{
  "jsonrpc": "2.0",
  "id": 13,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"isEnabled\":true,\"isVisible\":true,\"value\":\"42\",\"windows\":[{\"handle\":\"0x2B3C4D\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Agent reports to user:**
> Fix verified! 6 × 7 = 42 ✓

---

### Server Hot Reload (When MCP Server Code Changes)

If the *MCP server itself* is updated (not just the app), the agent must trigger a server restart:

**Step 1: Agent creates trigger file (from host)**
```powershell
"$(Get-Date -Format 'o')" | Out-File "C:\TransportTest\Shared\server.trigger"
```

**Step 2: Agent monitors signal file for new server_pid**
```
Agent reads: C:\TransportTest\Shared\mcp-ready.signal
```

Wait for `server_pid` to change:
```json
{
  "tcp_ip": "172.23.144.1",
  "tcp_port": 9999,
  "server_pid": 9999,
  "ready": true
}
```

**Step 3: Agent reconnects TCP**

Agent closes old TCP connection, opens new connection to same `tcp_ip:tcp_port`.

**Step 4: Agent re-initializes MCP**
```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{...}}
```

**Step 5: Agent re-launches app**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "launch_app",
    "arguments": { "path": "C:\\App\\Calculator.exe" }
  }
}
```

---

### Hot Reload Summary

| What Changed | Agent Action |
|--------------|--------------|
| App code only | `close_app` → `launch_app` (same TCP connection) |
| MCP server code | Create `server.trigger` → wait for signal → reconnect TCP → `launch_app` |
| Sandbox itself | Rare - just close and restart sandbox |

**Key insight:** App hot reload is fast (just close/launch). Server hot reload requires reconnection but keeps sandbox warm.

---

## Handling a Dialog

If clicking a button opens a dialog, the agent sees it in the `windows` response:

**Server → Agent (after clicking "Save"):**
```json
{
  "jsonrpc": "2.0",
  "id": 15,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":true,\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":false},{\"handle\":\"0x4D5E6F\",\"title\":\"Save As\",\"automationId\":\"SaveDialog\",\"bounds\":{\"x\":200,\"y\":150,\"width\":400,\"height\":300},\"isActive\":true}]}"
    }]
  }
}
```

**Agent notices:**
- New window appeared: "Save As" (`0x4D5E6F`)
- Main window is no longer active (`isActive: false`)
- Dialog is now active (`isActive: true`)

Agent can now target the dialog for next actions.

---

## Error Handling

**Window not found:**
```json
{
  "jsonrpc": "2.0",
  "id": 20,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":false,\"error\":\"Window not found: 'Notepad'\",\"partialMatches\":[\"Calculator\",\"Calculator - Result\"],\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true}]}"
    }]
  }
}
```

**Multiple windows match title:**
```json
{
  "jsonrpc": "2.0",
  "id": 21,
  "result": {
    "content": [{
      "type": "text",
      "text": "{\"success\":false,\"error\":\"Multiple windows match 'Calc': use windowHandle for precision\",\"matches\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\"},{\"handle\":\"0x7A8B9C\",\"title\":\"Calc.exe - Properties\"}],\"windows\":[{\"handle\":\"0x1A2B3C\",\"title\":\"Calculator\",\"automationId\":\"MainWindow\",\"bounds\":{\"x\":100,\"y\":100,\"width\":320,\"height\":480},\"isActive\":true},{\"handle\":\"0x7A8B9C\",\"title\":\"Calc.exe - Properties\",\"automationId\":\"\",\"bounds\":{\"x\":300,\"y\":200,\"width\":350,\"height\":400},\"isActive\":false}]}"
    }]
  }
}
```

Agent can use the specific `handle` from the matches to retry with precision.
