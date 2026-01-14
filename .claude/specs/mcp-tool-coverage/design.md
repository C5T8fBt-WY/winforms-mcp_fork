# MCP Tool Coverage for Autonomous Agent Support - Design

## 1. Overview

### What We're Building

We're extending the Rhombus.WinFormsMcp server to support autonomous UI exploration agents. The current implementation has 26 tools focused on direct manipulation (click, type, screenshot) but lacks critical observation, navigation, and safety features needed for LLM agents to autonomously explore Windows applications.

### Why

Autonomous agents need to:
1. **Observe**: Get hierarchical UI trees with configurable depth, not just flat element lists
2. **Navigate**: Expand menus, scroll lists, handle nested structures
3. **Self-heal**: Detect state changes, handle dynamic IDs, recover from failures
4. **Stay safe**: Run in sandboxed environments to prevent accidental data loss

Without these capabilities, agents cannot perform the five analysis lenses (test generation, bug detection, UX analysis, complexity calculation, documentation) that make them valuable.

### Architectural Decision (Based on Research)

**Decision**: MCP server runs **inside Windows Sandbox** alongside target application for full GUI automation with kernel-level safety.

**Rationale** (see Section 8.1 and `research-sandbox-architecture.md` for details):
- UI Automation cannot cross VM boundaries (Option A: MCP on host is infeasible)
- Windows Sandbox provides kernel-level filesystem isolation (ephemeral, Copy-on-Write)
- MCP server bootstrapped via `.wsb` LogonCommand, communicates via named pipes or mapped folders
- Sandbox has isolated `C:\` drive - host files physically inaccessible even if agent navigates File Explorer

**Trade-off**: 10-15s sandbox boot time per test session, but provides strongest filesystem safety guarantee.

**Background Automation**: Three-layer architecture enables agents to run while user continues using laptop normally (see `research-background-automation.md`):
1. **Virtual Display Driver (VDD)**: Installed in sandbox bootstrap, keeps DWM rendering when minimized
2. **Host Registry Key**: `RemoteDesktop_SuppressWhenMinimized = 2` prevents RDP session throttling
3. **InjectTouchInput**: More reliable than mouse injection in headless state (sandbox has Admin privileges)

### Success Metrics

- Agent generates 3+ test cases from exploring Calculator
- Agent detects bug (button with no state change) and reports it
- Agent runs in Windows Sandbox with no access to host `C:\Users\` folders
- `get_ui_tree()` returns <5000 tokens for depth=3 on complex apps

---

## 2. Architecture

### 2.1 High-Level System Design

```
┌─────────────────────────────────────────────────────────┐
│                    LLM Agent (Claude)                    │
│  - OODA Loop (Observe, Orient, Decide, Act)             │
│  - State graph tracking (visited nodes)                 │
│  - Analysis lenses (test gen, bug detection, etc.)      │
└────────────────────┬────────────────────────────────────┘
                     │ MCP Protocol (JSON-RPC over stdio)
                     ↓
┌─────────────────────────────────────────────────────────┐
│              MCP Server (Enhanced)                       │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Session Manager                                  │   │
│  │  - active_pid: int (PID scoping)                │   │
│  │  - element_cache: Dict[id, AutomationElement]   │   │
│  │  - event_queue: Queue[Event] (max 10)           │   │
│  │  - sandbox_handle: SandboxInstance?             │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Tool Registry (36 tools)                         │   │
│  │  NEW: get_ui_tree, expand_collapse, scroll       │   │
│  │  NEW: check_element_state, get_process_info     │   │
│  │  NEW: subscribe_to_events                        │   │
│  │  NEW: launch_app_sandboxed, close_sandbox       │   │
│  │  NEW: get_capabilities, validate_wsb             │   │
│  │  EXISTING: click_element, type_text, etc. (26)  │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Sandbox Manager                                  │   │
│  │  - Start/stop Windows Sandbox instances         │   │
│  │  - Validate .wsb configs (path safety)          │   │
│  │  - Monitor sandbox health                        │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Tree Builder & Pruner                            │   │
│  │  - Walk UIA tree with configurable depth        │   │
│  │  - Heuristic pruning (filter Pane, Group)       │   │
│  │  - Token budget enforcement (<5000 tokens)      │   │
│  └─────────────────────────────────────────────────┘   │
└────────────────────┬────────────────────────────────────┘
                     │ Windows API
                     ↓
┌─────────────────────────────────────────────────────────┐
│  Windows Sandbox (Isolated VM)                          │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Target Application (e.g., Notepad, Calculator)  │   │
│  │  - Isolated C:\ drive (no host filesystem)     │   │
│  │  - No network access                            │   │
│  │  - Mapped folders: C:\Inputs (RO), C:\Outputs   │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Virtual Display Driver (VDD)                    │   │
│  │  - Installed via LogonCommand (pnputil)        │   │
│  │  - Virtual monitor keeps DWM rendering active  │   │
│  │  - Enables background automation (minimized)   │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │ FlaUI / UIA Automation                           │   │
│  │  - UIA2 backend (no visual requirements)        │   │
│  │  - Element discovery, interaction, properties   │   │
│  │  - InjectTouchInput for reliable click/drag    │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### 2.2 Component Interaction Flow

**Exploration Flow** (typical agent workflow):
```
1. Agent → launch_app_sandboxed("notepad.exe", "test.wsb")
   MCP → SandboxManager.StartSandbox()
   MCP → AutomationHelper.LaunchApp() inside sandbox
   MCP ← {success: true, pid: 4402, sandbox_id: "sb_001"}

2. Agent → attach_to_process(4402)
   MCP → SessionManager.SetActivePID(4402)
   MCP ← {success: true, active_pid: 4402}

3. Agent → get_ui_tree(max_depth: 3)
   MCP → TreeBuilder.BuildTree(active_pid, depth=3)
   MCP → TreePruner.ApplyHeuristics(tree)
   MCP ← {tree: "<Window>...", token_count: 2341, pruned: 892 elements}

4. Agent → expand_collapse(selector: "File Menu", expand: true)
   MCP → AutomationHelper.FindElement(within_pid: 4402)
   MCP → element.Expand()
   MCP → Wait for StructureChangedEvent or 500ms timeout
   MCP ← {success: true, children_revealed: 12}

5. Agent → click_element(selector: "Save")
   MCP → AutomationHelper.Click()
   MCP → TakeScreenshot(after_action: true)
   MCP ← {success: true, state_changed: true, new_screenshot: "base64..."}

6. Agent → close_sandbox("sb_001")
   MCP → Extract artifacts from C:\AgentTestOutputs
   MCP → SandboxManager.TerminateSandbox("sb_001")
   MCP ← {success: true, artifacts: ["screenshot1.png", "log.txt"]}
```

**Security Flow** (cross-process defense):
```
1. Agent attached to Notepad (PID: 4402)
2. Agent clicks "Open File" → File Explorer opens (PID: 8821, different process)
3. Agent → get_ui_tree()
   MCP detects: top window PID (8821) != active_pid (4402)
   MCP ← {tree: "...", warning: {foreign_process_detected: {pid: 8821, name: "explorer.exe"}}}
4. Agent must explicitly: attach_to_process(8821, temporary: true) to interact
5. Inside sandbox: Explorer navigates to C:\Users\jhedin\Documents → sees empty sandbox directory, NOT host files
```

---

## 3. Components and Interfaces

### 3.1 New Tool: `get_ui_tree`

**Purpose**: Return hierarchical UIA tree with configurable depth and filtering

**Input Schema**:
```json
{
  "max_depth": {
    "type": "integer",
    "default": 3,
    "range": [1, 10],
    "description": "Tree traversal depth"
  },
  "include_offscreen": {
    "type": "boolean",
    "default": false,
    "description": "Include virtualized/scrolled-out elements"
  },
  "format": {
    "type": "string",
    "enum": ["xml", "json_compact"],
    "default": "xml"
  },
  "filter_by_pid": {
    "type": "integer",
    "optional": true,
    "description": "Override session active_pid, or null for desktop-wide"
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "tree": "<Window name='Calculator' id='1'>...</Window>",
  "format": "xml",
  "token_count": 2341,
  "element_count": 156,
  "pruned_count": 892,
  "max_depth_reached": true,
  "warnings": [
    {
      "type": "foreign_process_detected",
      "pid": 8821,
      "process_name": "explorer.exe",
      "reason": "File dialog opened"
    }
  ],
  "execution_time_ms": 234
}
```

**Implementation Notes**:
- Uses existing `AutomationHelper.GetElementTree()` but with PID filtering
- Applies heuristic pruning: removes `Pane`, `Group`, `Image` with no children
- Tracks token count (estimated: XML length * 0.25 tokens/char)
- If tree exceeds 5000 tokens, reduces max_depth and retries
- Caches window handles to avoid repeated desktop searches

### 3.2 New Tool: `expand_collapse`

**Purpose**: Expand/collapse tree nodes or menus to reveal hidden children

**Input Schema**:
```json
{
  "selector_type": {
    "type": "string",
    "enum": ["automation_id", "name"]
  },
  "selector_value": {
    "type": "string"
  },
  "expand": {
    "type": "boolean",
    "description": "true = expand, false = collapse"
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "children_revealed": 12,
  "ui_tree_updated": true,
  "new_state": "expanded",
  "execution_time_ms": 145
}
```

**Implementation Notes**:
- Finds element via `FindByAutomationId` or `FindByName` (scoped to active_pid)
- Checks if element supports `ExpandCollapsePattern`
- Invokes `Expand()` or `Collapse()` method
- Waits for `StructureChangedEvent` or 500ms timeout
- Returns count of newly visible children

### 3.3 New Tool: `scroll`

**Purpose**: Scroll virtualized lists to reveal off-screen items

**Input Schema**:
```json
{
  "selector_type": {
    "type": "string",
    "enum": ["automation_id", "name"]
  },
  "selector_value": {
    "type": "string",
    "description": "Container that supports ScrollPattern"
  },
  "direction": {
    "type": "string",
    "enum": ["up", "down", "left", "right"]
  },
  "amount": {
    "type": "string",
    "enum": ["line", "page", "end"]
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "scroll_percent": 45.5,
  "new_visible_items": ["Item 10", "Item 11", "Item 12"],
  "at_boundary": false,
  "execution_time_ms": 89
}
```

**Implementation Notes**:
- Finds container element (scoped to active_pid)
- Checks if supports `ScrollPattern`
- Invokes `Scroll(direction, amount)` method
- Queries `VerticalScrollPercent` or `HorizontalScrollPercent`
- Re-scans children to identify newly visible items

### 3.4 New Tool: `check_element_state`

**Purpose**: Unified state check (exists, enabled, visible, focused, rect)

**Input Schema**:
```json
{
  "selector_type": {
    "type": "string",
    "enum": ["automation_id", "name", "cached_id"]
  },
  "selector_value": {
    "type": "string"
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "exists": true,
  "is_enabled": true,
  "is_visible": true,
  "is_focused": false,
  "bounding_rect": [100, 200, 300, 250],
  "execution_time_ms": 23
}
```

**Implementation Notes**:
- Fast path: if `selector_type == "cached_id"`, retrieve from `SessionManager.element_cache`
- Otherwise: search via `FindByAutomationId` or `FindByName` (scoped to active_pid)
- Query UIA properties: `IsEnabled`, `IsOffscreen`, `HasKeyboardFocus`, `BoundingRectangle`
- Completes in <100ms (requirement NFR-3)

### 3.5 New Tool: `get_process_info`

**Purpose**: Get process metadata for window targeting and continuity

**Input Schema**:
```json
{
  "window_handle": {
    "type": "string",
    "description": "HWND in hex format (e.g., '0x0004A3B8')"
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "pid": 4402,
  "process_name": "notepad.exe",
  "is_responding": true,
  "window_state": "normal",
  "main_window_handle": "0x0004A3B8",
  "execution_time_ms": 12
}
```

**Implementation Notes**:
- Convert hex HWND to `IntPtr`
- Query `GetWindowThreadProcessId()` Win32 API
- Get `Process.GetProcessById(pid)`
- Check `process.Responding` property
- Query window state via `GetWindowPlacement()` API

### 3.6 New Tool: `subscribe_to_events`

**Purpose**: Enable event monitoring (popups, toasts, dialogs)

**Input Schema**:
```json
{
  "event_types": {
    "type": "array",
    "items": {
      "enum": ["window_opened", "toast_shown", "dialog_shown", "structure_changed"]
    }
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "subscribed_to": ["window_opened", "toast_shown"],
  "queue_max_size": 10,
  "message": "Events will be injected into next get_ui_tree() call"
}
```

**Implementation Notes**:
- Uses FlaUI's `Automation.RegisterStructureChangedEvent()`
- Maintains internal `Queue<Event>` (max size: 10)
- Does NOT push events via stdout (MCP is request/response, not streaming)
- Next `get_ui_tree()` call injects high-priority notifications at tree root:
  ```xml
  <Window>
    <Notification type="toast_shown" timestamp="..." message="File saved" />
    <!-- normal tree continues -->
  </Window>
  ```

### 3.7 New Tool: `launch_app_sandboxed`

**Purpose**: Launch app in Windows Sandbox with .wsb config validation

**Input Schema**:
```json
{
  "app_path": {
    "type": "string",
    "description": "Path inside sandbox (e.g., 'C:\\Inputs\\myapp.exe')"
  },
  "sandbox_config_path": {
    "type": "string",
    "description": "Path to .wsb file on host (e.g., 'C:\\Configs\\test.wsb')"
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "sandbox_id": "sb_001",
  "pid": 4402,
  "message": "App launched in isolated sandbox. Use close_sandbox() to clean up.",
  "execution_time_ms": 3456
}
```

**Implementation Notes**:
- Validates .wsb config via `SandboxManager.ValidateConfig()`:
  - Check `<Networking>` is `Disable`
  - Check no mapped folders point to sensitive paths (`C:\Users\*\Documents`, etc.)
  - Return error if violations detected
- Launches Windows Sandbox via `WindowsSandbox.exe` with config
- Waits for sandbox to boot (typically 10-15 seconds)
- Polls for process with matching name inside sandbox
- Returns sandbox ID for later `close_sandbox()` call

### 3.8 New Tool: `close_sandbox`

**Purpose**: Terminate sandbox and extract artifacts

**Input Schema**:
```json
{
  "sandbox_id": {
    "type": "string",
    "description": "ID returned from launch_app_sandboxed"
  }
}
```

**Output Schema**:
```json
{
  "success": true,
  "artifacts_extracted": ["screenshot1.png", "log.txt", "test_results.json"],
  "output_folder": "C:\\AgentTestOutputs",
  "message": "Sandbox terminated. All state destroyed.",
  "execution_time_ms": 1234
}
```

**Implementation Notes**:
- Extracts files from mapped output folder (`C:\Outputs` → `C:\AgentTestOutputs`)
- Terminates Windows Sandbox process (all state inside sandbox is lost)
- Cleans up sandbox ID from `SessionManager.sandbox_handles`

### 3.9 New Tool: `get_capabilities`

**Purpose**: Detect MCP server capabilities (sandboxing, OS version, etc.)

**Input Schema**: None (no arguments)

**Output Schema**:
```json
{
  "success": true,
  "sandbox_available": true,
  "os_version": "Windows 11 Pro 23H2",
  "flaui_version": "4.0.0",
  "uia_backend": "UIA2",
  "max_depth_supported": 10,
  "token_budget": 5000,
  "features": ["get_ui_tree", "expand_collapse", "scroll", "sandboxing"]
}
```

**Implementation Notes**:
- Checks if Windows Sandbox is available: `Get-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM"`
- Returns OS version via `Environment.OSVersion`
- Lists all available MCP tools in `features` array

---

## 4. Data Models

### 4.1 Session State

```csharp
public class SessionManager
{
    public int? ActivePID { get; set; }
    public Dictionary<string, AutomationElement> ElementCache { get; set; }
    public Queue<UiEvent> EventQueue { get; set; }
    public Dictionary<string, SandboxInstance> SandboxHandles { get; set; }
    public AutomationHelper AutomationHelper { get; set; }

    public AutomationElement? GetMainWindow(int pid)
    {
        // Returns cached window or searches for it
    }

    public void SetActivePID(int pid)
    {
        ActivePID = pid;
        // Clear element cache when switching processes
        ElementCache.Clear();
    }
}
```

### 4.2 UiEvent (for event queue)

```csharp
public class UiEvent
{
    public string Type { get; set; } // "window_opened", "toast_shown", etc.
    public DateTime Timestamp { get; set; }
    public string WindowTitle { get; set; }
    public int? ProcessId { get; set; }
    public int? AutoDismissTimeout { get; set; } // ms
}
```

### 4.3 SandboxInstance

```csharp
public class SandboxInstance
{
    public string SandboxId { get; set; } // "sb_001"
    public Process SandboxProcess { get; set; }
    public int TargetAppPid { get; set; }
    public string ConfigPath { get; set; } // .wsb file used
    public DateTime StartedAt { get; set; }
}
```

### 4.4 TreeNode (for pruning logic)

```csharp
public class TreeNode
{
    public string AutomationId { get; set; }
    public string Name { get; set; }
    public string ControlType { get; set; }
    public Rectangle BoundingRectangle { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsOffscreen { get; set; }
    public List<TreeNode> Children { get; set; }

    public bool ShouldPrune()
    {
        // Heuristic: Prune Pane/Group with no interactive children
        if ((ControlType == "Pane" || ControlType == "Group") &&
            !Children.Any(c => c.IsInteractive()))
            return true;
        return false;
    }

    public bool IsInteractive()
    {
        return ControlType == "Button" || ControlType == "Edit" ||
               ControlType == "ComboBox" || ControlType == "CheckBox" ||
               ControlType == "MenuItem";
    }
}
```

---

## 5. Error Handling

### 5.1 Error Categories and Recovery

**ElementNotFoundException** (most common):
```json
{
  "success": false,
  "error": "Element not found: AutomationId='SubmitBtn_123'",
  "error_code": "ELEMENT_NOT_FOUND",
  "recovery_suggestions": [
    "Element may have dynamic ID suffix. Try name-based selector.",
    "Element may be offscreen. Try scroll() to reveal it.",
    "Element may be in collapsed menu. Try expand_collapse() first."
  ],
  "last_known_state": "UI tree snapshot before action",
  "execution_time_ms": 5000
}
```

**InvalidWindowHandleException**:
```json
{
  "success": false,
  "error": "Window no longer exists: HWND=0x0004A3B8",
  "error_code": "INVALID_WINDOW_HANDLE",
  "recovery_suggestions": [
    "Window may have closed. Re-attach to process via active PID.",
    "Window may have been replaced (dialog closed). Call get_ui_tree() to refresh."
  ],
  "active_pid": 4402,
  "execution_time_ms": 123
}
```

**TreeTooBigException** (performance):
```json
{
  "success": false,
  "error": "UI tree exceeds token budget (8234 tokens > 5000 limit)",
  "error_code": "TREE_TOO_BIG",
  "recovery_suggestions": [
    "Reduce max_depth (currently 5, try 3).",
    "Target specific window/panel instead of entire desktop.",
    "Pruning removed 892 elements but still exceeded budget."
  ],
  "tree_stats": {
    "element_count": 2341,
    "token_count": 8234,
    "max_depth_reached": 5,
    "pruned_count": 892
  },
  "execution_time_ms": 3456
}
```

**SandboxNotAvailableException**:
```json
{
  "success": false,
  "error": "Windows Sandbox feature is not enabled on this system",
  "error_code": "SANDBOX_NOT_AVAILABLE",
  "recovery_suggestions": [
    "Enable Windows Sandbox: DISM /Online /Enable-Feature /FeatureName:Containers-DisposableClientVM",
    "Or run without sandboxing (UNSAFE - confirm with user first)."
  ],
  "execution_time_ms": 45
}
```

**UnsafeConfigException**:
```json
{
  "success": false,
  "error": "Unsafe .wsb config: Mapped folder exposes sensitive path 'C:\\Users\\jhedin\\Documents'",
  "error_code": "UNSAFE_WSB_CONFIG",
  "config_path": "C:\\Configs\\test.wsb",
  "violations": [
    "MappedFolder[0]: HostFolder points to sensitive path"
  ],
  "recovery_suggestions": [
    "Use dedicated test folder instead (e.g., C:\\AgentTestInputs).",
    "Or use --allow-dangerous-mappings flag (requires explicit confirmation)."
  ],
  "execution_time_ms": 89
}
```

### 5.2 Retry Strategy

**ElementNotFoundException**:
- Retry 3 times with 100ms intervals (for timing issues)
- After 3 failures, return error with suggestions

**InvalidWindowHandleException**:
- Retry 1 time: re-query process windows via active PID
- If still fails, return error

**TreeTooBigException**:
- Auto-retry with max_depth - 1 (down to minimum of 2)
- If still exceeds budget at depth=2, return error with partial tree

**TimeoutException** (async operations like wait_for_element):
- No retry - return timeout error immediately
- Suggested timeout: 10000ms for async waits, 5000ms for find operations

---

## 6. Testing Strategy

### 6.1 Unit Tests

**Test Target**: Individual tool implementations

**Framework**: NUnit (existing test suite)

**Key Test Cases**:

**TreeBuilder.BuildTree()**:
```csharp
[Test]
public void BuildTree_WithMaxDepth3_ReturnsCorrectDepth()
{
    // Arrange: Mock AutomationElement with 5 levels deep
    // Act: BuildTree(maxDepth: 3)
    // Assert: Tree has exactly 3 levels, no deeper
}

[Test]
public void BuildTree_AppliesPruning_RemovesNonInteractiveContainers()
{
    // Arrange: Tree with 10 Pane elements with no interactive children
    // Act: BuildTree() with pruning enabled
    // Assert: Pruned count = 10, element count reduced
}

[Test]
public void BuildTree_ExceedsTokenBudget_AutoReducesDepth()
{
    // Arrange: Large tree (SAP GUI simulator)
    // Act: BuildTree(maxDepth: 5, tokenBudget: 5000)
    // Assert: Auto-retries with depth=4, then 3, until under budget
}
```

**SandboxManager.ValidateConfig()**:
```csharp
[Test]
public void ValidateConfig_WithSensitivePath_ReturnsError()
{
    // Arrange: .wsb with <HostFolder>C:\Users\jhedin\Documents</HostFolder>
    // Act: ValidateConfig()
    // Assert: Error = "Unsafe .wsb config", violations listed
}

[Test]
public void ValidateConfig_WithNetworkingEnabled_ReturnsError()
{
    // Arrange: .wsb with <Networking>Enable</Networking>
    // Act: ValidateConfig()
    // Assert: Error = "Networking must be disabled for agent testing"
}
```

**SessionManager.SetActivePID()**:
```csharp
[Test]
public void SetActivePID_ClearsElementCache()
{
    // Arrange: Cache has 5 elements from old PID
    // Act: SetActivePID(new_pid)
    // Assert: ElementCache.Count == 0
}
```

### 6.2 Integration Tests

**Test Target**: Tool interactions with real WinForms app

**Setup**: Launch `Rhombus.WinFormsMcp.TestApp` (existing test application)

**Key Test Cases**:

**Agent Exploration Flow**:
```csharp
[Test]
public async Task Agent_CanExploreTestApp_GeneratesTree()
{
    // Arrange: Launch TestApp
    var launchResult = await mcp.LaunchApp("TestApp.exe");
    await mcp.AttachToProcess(launchResult.pid);

    // Act: Get UI tree
    var tree = await mcp.GetUiTree(maxDepth: 3);

    // Assert
    Assert.IsTrue(tree.success);
    Assert.Less(tree.token_count, 5000);
    Assert.IsTrue(tree.tree.Contains("<Button"));
}

[Test]
public async Task Agent_CanExpandMenu_RevealsChildren()
{
    // Arrange: TestApp with File menu
    await SetupTestApp();

    // Act: Expand File menu
    var result = await mcp.ExpandCollapse("File Menu", expand: true);

    // Assert
    Assert.IsTrue(result.success);
    Assert.Greater(result.children_revealed, 0);

    // Verify children are now visible in tree
    var tree = await mcp.GetUiTree();
    Assert.IsTrue(tree.tree.Contains("Save"));
}

[Test]
public async Task Agent_CanScrollList_RevealsNewItems()
{
    // Arrange: TestApp with 1000-item list
    await SetupTestAppWithLargeList();

    // Act: Scroll down
    var result = await mcp.Scroll("ItemList", direction: "down", amount: "page");

    // Assert
    Assert.IsTrue(result.success);
    Assert.Greater(result.new_visible_items.Count, 0);
    Assert.Greater(result.scroll_percent, 0);
}
```

**Security Tests**:
```csharp
[Test]
public async Task GetUiTree_WithActivePID_OnlyReturnsProcessWindows()
{
    // Arrange: Launch TestApp + Calculator
    var testAppPid = (await mcp.LaunchApp("TestApp.exe")).pid;
    var calcPid = (await mcp.LaunchApp("calc.exe")).pid;
    await mcp.AttachToProcess(testAppPid);

    // Act: Get tree (should only show TestApp)
    var tree = await mcp.GetUiTree();

    // Assert
    Assert.IsTrue(tree.tree.Contains("TestApp"));
    Assert.IsFalse(tree.tree.Contains("Calculator")); // Not visible!
}

[Test]
public async Task ValidateWsbConfig_RejectsDocumentsFolder()
{
    // Arrange: .wsb that maps C:\Users\jhedin\Documents
    var unsafeConfig = CreateUnsafeWsbConfig();

    // Act: Try to launch
    var result = await mcp.LaunchAppSandboxed("notepad.exe", unsafeConfig);

    // Assert
    Assert.IsFalse(result.success);
    Assert.That(result.error, Does.Contain("Unsafe .wsb config"));
    Assert.That(result.error, Does.Contain("Documents"));
}
```

### 6.3 E2E Tests (Agent Workflow)

**Test Target**: Full agent exploration workflow

**Setup**: Mock LLM agent with predefined exploration script

**Key Test Cases**:

**Calculator Exploration**:
```csharp
[Test]
public async Task Agent_ExploresCalculator_GeneratesTestCases()
{
    // Simulate agent workflow
    var agent = new MockAgent(mcp);

    // Phase 1: Launch
    await agent.LaunchApp("calc.exe");

    // Phase 2: Explore (OODA loop)
    var testCases = await agent.ExploreAndGenerateTests();

    // Assert
    Assert.GreaterOrEqual(testCases.Count, 3); // Requirement: 3+ test cases
    Assert.IsTrue(testCases.Any(tc => tc.title.Contains("addition")));
    Assert.IsTrue(testCases.Any(tc => tc.title.Contains("clear")));
}

[Test]
public async Task Agent_DetectsBug_ReportsWithReproSteps()
{
    // Arrange: TestApp with buggy "Submit" button (does nothing)
    await SetupTestAppWithBug();

    // Act: Agent explores
    var agent = new MockAgent(mcp);
    var bugs = await agent.ExploreAndDetectBugs();

    // Assert
    Assert.GreaterOrEqual(bugs.Count, 1);
    var submitBug = bugs.First(b => b.element.Contains("Submit"));
    Assert.That(submitBug.description, Does.Contain("no state change"));
    Assert.IsTrue(submitBug.reproSteps.Count > 0);
}
```

**Sandbox Safety**:
```csharp
[Test]
public async Task Agent_InSandbox_CannotAccessHostFiles()
{
    // Arrange: Create test file on host
    File.WriteAllText("C:\\Users\\jhedin\\Documents\\important.txt", "SECRET");

    // Act: Launch Notepad in sandbox
    var wsb = CreateSafeWsbConfig();
    await mcp.LaunchAppSandboxed("notepad.exe", wsb);

    // Agent tries to open file via File > Open > browse to Documents
    var agent = new MockAgent(mcp);
    await agent.ClickMenu("File", "Open");
    await agent.Navigate("C:\\Users\\jhedin\\Documents");

    // Assert: Directory is empty (sandbox's isolated C:\)
    var tree = await mcp.GetUiTree();
    Assert.IsFalse(tree.tree.Contains("important.txt"));

    // Cleanup
    File.Delete("C:\\Users\\jhedin\\Documents\\important.txt");
}
```

### 6.4 Performance Tests

**Test Target**: NFR-1, NFR-2 (speed and token budget)

```csharp
[Test]
public async Task GetUiTree_OnComplexApp_CompletesUnder2Seconds()
{
    // Arrange: Launch complex app (Visual Studio simulator)
    await LaunchComplexApp();

    // Act
    var stopwatch = Stopwatch.StartNew();
    var tree = await mcp.GetUiTree(maxDepth: 3);
    stopwatch.Stop();

    // Assert: NFR-1
    Assert.Less(stopwatch.ElapsedMilliseconds, 2000);
    Assert.IsTrue(tree.success);
}

[Test]
public async Task GetUiTree_EnforcesTokenBudget()
{
    // Arrange: Large app
    await LaunchComplexApp();

    // Act
    var tree = await mcp.GetUiTree(maxDepth: 3);

    // Assert: NFR-2
    Assert.Less(tree.token_count, 5000);
    if (tree.pruned_count > 0)
    {
        Console.WriteLine($"Pruned {tree.pruned_count} elements to meet budget");
    }
}
```

---

## 7. Migration Strategy

### 7.1 Backward Compatibility

**Existing tools MUST continue working unchanged**:
- `click_element`, `type_text`, `take_screenshot`, etc. (26 existing tools)
- No breaking changes to existing schemas

**Deprecation Path** (if needed):
- Mark `list_elements` as deprecated (superseded by `get_ui_tree`)
- Return warning in response: `"deprecated": true, "use_instead": "get_ui_tree"`
- Keep `list_elements` functional for 2 major versions

### 7.2 Phased Rollout

**Phase 1 (P0 Tools - Core Functionality)**:
- `get_ui_tree` (hierarchical tree with pruning)
- `expand_collapse` (menu exploration)
- `scroll` (virtualized list navigation)
- `check_element_state` (unified state check)
- PID scoping in `SessionManager`

**Phase 2 (P0 Tools - Safety)**:
- `launch_app_sandboxed` (Windows Sandbox integration)
- `close_sandbox`
- `get_capabilities` (sandbox detection)
- .wsb config validation

**Phase 3 (P1 Tools - Advanced)**:
- `get_process_info` (cross-process detection)
- `subscribe_to_events` (event queue)
- Post-action state feedback (screenshots/digests)

**Phase 4 (P2 Tools - Nice-to-have)**:
- `find_element_near_anchor` (anchor-based selectors)
- Regex/wildcard AutomationId patterns
- `confirm_action` (destructive action confirmation)

### 7.3 Testing During Migration

**After each phase**:
1. Run full existing test suite (ensure no regressions)
2. Run new integration tests for added tools
3. Run E2E agent workflow test
4. Performance benchmark: ensure `get_ui_tree` < 2s on complex apps

---

## 8. Architecture Decision & Research Findings

### 8.1 Windows Sandbox Architecture (RESOLVED)

**Research Source**: See `research-sandbox-architecture.md` for comprehensive analysis.

**DECISION**: **Option B - MCP Server Inside Sandbox** is the recommended approach for GUI automation with safety.

**Key Findings**:

1. **Windows Sandbox Provides Kernel-Level Isolation**:
   - Uses Microsoft Hypervisor with "integrated scheduling" (shares host kernel, fast startup)
   - Copy-on-Write memory: Any sandbox modification is volatile, discarded on termination
   - Isolated filesystem: Sandbox has its own `C:\` drive, host `C:\Users\` does NOT exist inside sandbox
   - **Direct Map technology**: Host system files are mapped read-only into sandbox memory

2. **Why Option A (MCP on Host) Won't Work**:
   - UI Automation cannot cross VM boundaries
   - Windows Sandbox runs in Session 0 isolation (Hyper-V VM)
   - `Automation.FromHandle(HWND)` from host cannot see sandbox windows
   - **Verdict**: Technically infeasible for GUI automation

3. **Why Option B (MCP Inside Sandbox) is Recommended**:
   - MCP server runs inside sandbox VM alongside target application
   - FlaUI/UIA works normally (same VM, same session)
   - Full GUI support including File Explorer dialogs, system dialogs, etc.
   - **Communication**: MCP server is bootstrapped via `.wsb` LogonCommand, communicates via mapped folders OR named pipes (research needed for final transport choice)

4. **Why Option C (RDP-based) is Fallback Only**:
   - Loses structural UIA tree (no AutomationId, Name, ControlType)
   - Falls back to pixel-based automation (OmniParser, OCR)
   - Defeats purpose of MCP tool-based exploration
   - **Use only if**: Option B transport proves impractical

**Implementation Strategy** (Option B):
```
1. Host prepares .wsb configuration file with:
   - <Networking>Disable</Networking> (air-gapped environment)
   - <MappedFolder> for MCP server binary + VDD driver files (read-only)
   - <MappedFolder> for input data (read-only)
   - <MappedFolder> for output artifacts (write-only)
   - <LogonCommand> to run bootstrap script

2. Host launches sandbox: WindowsSandbox.exe config.wsb

3. Sandbox boots (10-15s):
   - WDAGUtilityAccount logs in automatically (has Admin privileges)
   - LogonCommand executes bootstrap script:
     a. Import VDD driver certificate: Import-Certificate -FilePath "C:\MCP\vdd_driver.cer" -CertStoreLocation Cert:\LocalMachine\Root
     b. Install VDD driver: pnputil /add-driver "C:\MCP\vdd_driver.inf" /install
     c. Wait for driver initialization (5s)
     d. Launch MCP server: C:\MCP\Rhombus.WinFormsMcp.Server.exe
   - MCP server inside sandbox listens for requests

4. Host LLM agent communicates with sandbox MCP via:
   - Option 1 (preferred): Named pipe shared across VM boundary (requires testing)
   - Option 2 (fallback): Shared folder polling (host writes request.json to mapped folder, sandbox writes response.json)

5. Agent sends tool requests → Sandbox MCP executes FlaUI commands → Returns results

6. Host calls close_sandbox() → Sandbox VM terminates → All state lost (ephemeral)
```

**Resolved Questions**:

- ✅ **Q1 (Sandbox Architecture)**: MCP runs inside sandbox for full GUI support
- ✅ **Q2 (Programmatic Control)**: Use LogonCommand to bootstrap MCP server, one app per sandbox instance (simplest approach)
- ✅ **Q3 (PID Translation)**: Not needed - PID management happens entirely inside sandbox
- ⚠️ **Q4 (MCP Transport)**: Requires prototype testing - named pipe vs shared folder polling

**Remaining Open Question**:

**Q4: MCP Transport Across VM Boundary** (REQUIRES PROTOTYPE):
- **Question**: Which transport mechanism for Host ↔ Sandbox MCP communication?
- **Option 1 (Named Pipe)**:
  - Windows supports named pipes across local machine boundaries
  - MCP server inside sandbox creates named pipe: `\\.\pipe\mcp-sandbox-<sandbox_id>`
  - Host connects to pipe, sends JSON-RPC requests
  - **Pros**: Low latency (~1-5ms), bidirectional, streaming support
  - **Cons**: Requires testing if Windows Sandbox allows cross-VM named pipes
  - **Research Needed**: Prototype named pipe communication across sandbox boundary
- **Option 2 (Shared Folder Polling)**:
  - Host writes `request-<timestamp>.json` to mapped folder
  - Sandbox MCP watches folder (FileSystemWatcher), reads requests
  - Sandbox writes `response-<timestamp>.json` to mapped folder
  - Host polls for response file
  - **Pros**: Simple, guaranteed to work (folder mapping is core Windows Sandbox feature)
  - **Cons**: High latency (~50-200ms per request due to file I/O), no streaming
- **Option 3 (TCP with Loopback Exception)**:
  - Enable `<Networking>Enable</Networking>` with Windows Firewall rule allowing ONLY 127.0.0.1
  - MCP server inside sandbox listens on localhost:8080
  - Host connects to exposed port
  - **Pros**: Standard MCP transport, low latency
  - **Cons**: Violates network isolation requirement (security risk), requires firewall config
  - **Verdict**: Not recommended unless Options 1 and 2 fail

**Recommendation**: Prototype Option 1 (named pipe) first. If not supported by Windows Sandbox architecture, fall back to Option 2 (shared folder polling).

**Non-Critical Questions**:

5. **DPI Scaling**: Do we have test cases for 150%, 175%, 200% DPI settings? Does FlaUI normalize coordinates automatically?
   - **Mitigation**: Add DPI test cases, document if manual normalization needed

6. **Windows Sandbox Availability**: What percentage of target users have Windows Sandbox enabled?
   - **Mitigation**: `get_capabilities()` warns if unavailable, fallback to unsandboxed with confirmation

7. **Token Estimation Accuracy**: Is `XML length * 0.25` accurate for token count?
   - **Mitigation**: Test with real LLM tokenizer, adjust formula if needed

8. **Event Queue Size**: Is 10 events sufficient, or do we need configurable size?
   - **Mitigation**: Start with 10, add configuration parameter if needed

### 8.2 Background Automation Architecture (RESOLVED)

**Research Source**: See `research-background-automation.md` for comprehensive analysis.

**PROBLEM**: Windows Sandbox uses RDP internally. When the sandbox window is minimized, the RDP client signals the guest to stop rendering, which breaks UI automation (elements become invisible, screenshots are black).

**SOLUTION**: Three-layer architecture enables reliable background automation:

| Layer | Component | Purpose | Implementation |
|-------|-----------|---------|----------------|
| 1. Display | Virtual Display Driver (IDD/VDD) | Provides virtual monitor target so DWM keeps rendering | `pnputil /add-driver vdd_driver.inf /install` in LogonCommand |
| 2. Session | Host Registry Key | Prevents RDP client from throttling guest when minimized | `RemoteDesktop_SuppressWhenMinimized = 2` (one-time host setup) |
| 3. Input | `InjectTouchInput` API | Reliable input injection even in headless state | Already implemented in Phase 4 (touch/pen tools) |

**Why Each Layer is Necessary**:
- **Layer 1 alone**: DWM renders to virtual display, but RDP client may still throttle session
- **Layer 2 alone**: RDP doesn't throttle, but DWM has no render target (black screenshots)
- **Layer 3 alone**: Mouse input unreliable when window is not focused
- **All three**: DWM renders continuously, session stays active, input works reliably

**VDD Driver Options**:
1. **IddSampleDriver** (Microsoft sample): Open-source, demonstrates IddCx framework
2. **Virtual Display Manager**: Commercial option with better stability
3. **Custom UMDF driver**: Full control but requires driver signing

**Files Required in MCP Server Package**:
- `vdd_driver.inf` - Driver installation manifest
- `vdd_driver.sys` - UMDF driver binary (or .dll for newer UMDF)
- `vdd_driver.cer` - Driver signing certificate (for test signing)
- `bootstrap.ps1` - PowerShell script for LogonCommand

**Bootstrap Script Template**:
```powershell
# C:\MCP\bootstrap.ps1 - Run as LogonCommand in sandbox

# 1. Trust the driver certificate (required for test-signed drivers)
Import-Certificate -FilePath "C:\MCP\vdd_driver.cer" -CertStoreLocation Cert:\LocalMachine\Root

# 2. Install the Virtual Display Driver
pnputil /add-driver "C:\MCP\vdd_driver.inf" /install

# 3. Wait for driver to initialize and display to appear
Start-Sleep -Seconds 5

# 4. Launch the MCP server
Start-Process -FilePath "C:\MCP\Rhombus.WinFormsMcp.Server.exe" -NoNewWindow
```

**Verification Steps** (for testing VDD installation):
1. After sandbox boots, check Device Manager for "Virtual Display Adapter"
2. Check Display Settings for additional monitor (VDD creates virtual display)
3. Minimize sandbox window, take screenshot - should NOT be black
4. Run UI automation while minimized - should succeed

### 8.3 Technical Risks

**Risk 1: Sandbox Boot Time**
- **Impact**: Adds 10-15s latency to `launch_app_sandboxed()`
- **Mitigation**: Document expected latency, explore caching warm sandbox instances

**Risk 2: .wsb Validation Brittleness**
- **Impact**: XML parsing could fail on unusual .wsb formats
- **Mitigation**: Use robust XML parser, handle parse errors gracefully

**Risk 3: Tree Pruning Too Aggressive**
- **Impact**: Agent misses important elements due to over-pruning
- **Mitigation**: Make pruning configurable via `get_ui_tree(pruning_level: "conservative" | "aggressive")`

**Risk 4: Cross-Process Dialog Detection**
- **Impact**: Hard to reliably detect all cross-process scenarios (WoW64, Print, etc.)
- **Mitigation**: Start with basic PID mismatch detection, iterate based on agent feedback

**Risk 5: VDD Driver Installation Failure**
- **Impact**: Background automation fails (black screenshots, invisible elements when minimized)
- **Mitigation**:
  - Detect VDD installation failure in bootstrap, report via `get_capabilities()`
  - Provide fallback warning: "Keep sandbox window visible for reliable automation"
  - Consider bundling multiple VDD driver options (IddSampleDriver, alternative)

**Risk 6: VDD Driver Signing**
- **Impact**: Windows may reject unsigned/test-signed drivers depending on boot configuration
- **Mitigation**:
  - Use properly signed driver (EV certificate) for production
  - For development: Import test certificate in bootstrap, enable test signing if needed
  - Document that sandbox typically has relaxed signing requirements

---

## Summary

This design extends the WinForms MCP with 10 new tools (P0/P1) while maintaining backward compatibility with 26 existing tools. The architecture introduces:

- **Session-level state management** (active PID, element cache, event queue)
- **Hierarchical tree inspection** with token budget enforcement
- **Sandbox integration** with .wsb validation for filesystem isolation
- **Defense-in-depth security** (filesystem isolation, PID scoping, network disabled, confirmations)

The implementation is phased over 4 releases, with comprehensive testing at unit, integration, E2E, and performance levels. The design ensures agents can safely explore Windows applications without risk of data loss, generating test cases, detecting bugs, and analyzing UX gaps.
