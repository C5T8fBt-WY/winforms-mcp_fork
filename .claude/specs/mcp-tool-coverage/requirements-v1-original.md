# MCP Tool Coverage for Autonomous Agent Support - Requirements

## 1. Introduction

This specification defines the required MCP tool coverage for the Rhombus.WinFormsMcp server to support autonomous UI exploration agents. The goal is to enable LLM agents to explore Windows applications, identify bugs, generate test cases, analyze UX gaps, and produce comprehensive documentation without human intervention.

**Current State**: The WinForms MCP has 26 tools focused on direct UI manipulation (clicks, text input, screenshots) but lacks critical tools for autonomous agent workflows like hierarchical tree inspection, progressive disclosure, and event monitoring.

**Target State**: A complete MCP tool suite that matches the capabilities described in battle-tested agent frameworks (Anthropic Computer Use, Stagehand, Skyvern, Microsoft UFO) and supports the five analysis lenses (test generation, bug detection, UX analysis, complexity calculation, documentation).

---

## 2. User Stories

### 2.1 Core Observation & Navigation

**2.1.1** WHEN an agent needs to understand the current application state, it SHALL call `get_ui_tree` with configurable depth and filtering options
- **Acceptance Criteria**:
  - Returns hierarchical UIA tree in XML or JSON format
  - Supports `max_depth` parameter (default: 3, range: 1-10)
  - Supports `include_offscreen` parameter (default: false)
  - Filters out non-interactive containers (Pane, Group) by default
  - Returns element AutomationId, Name, ControlType, BoundingRectangle, IsEnabled
  - Tree payload size SHALL NOT exceed 5000 tokens for depth=3
  - **Current Gap**: Have `list_elements` but returns flat list, not hierarchical tree with configurable filtering

**2.1.2** WHEN an agent encounters a collapsed menu or tree node, it SHALL call `expand_collapse` to reveal hidden children
- **Acceptance Criteria**:
  - Accepts element selector (automation_id or name)
  - Accepts `expand` boolean parameter
  - Returns success status and count of children revealed
  - Waits for StructureChangedEvent or 500ms timeout
  - **Current Gap**: Not implemented - agents cannot explore nested menus

**2.1.3** WHEN an agent needs to scroll through a virtualized list, it SHALL call `scroll` to reveal off-screen items
- **Acceptance Criteria**:
  - Accepts element selector for scrollable container
  - Accepts `direction` enum: up, down, left, right
  - Accepts `amount` enum: line, page, end
  - Returns current scroll percentage (0-100)
  - Returns list of newly visible item names/IDs
  - **Current Gap**: Not implemented - agents cannot explore large lists

### 2.2 State Validation & Synchronization

**2.2.1** WHEN an agent needs to verify element state before interaction, it SHALL call `check_element_state`
- **Acceptance Criteria**:
  - Accepts element selector
  - Returns: exists (bool), is_enabled (bool), is_visible (bool), is_focused (bool)
  - Returns bounding_rect [x, y, width, height]
  - Completes in <100ms
  - **Current Gap**: Have `element_exists` and `get_property` but no unified state check

**2.2.2** WHEN an agent needs process information for window targeting, it SHALL call `get_process_info`
- **Acceptance Criteria**:
  - Accepts window_handle (HWND)
  - Returns: pid, process_name, is_responding, window_state (normal/minimized/maximized)
  - **Current Gap**: Not implemented - agents cannot track process continuity

**2.2.3** WHEN an agent completes an action, it SHALL receive post-action state snapshot automatically
- **Acceptance Criteria**:
  - All interaction tools (click, type, etc.) return: success (bool), error (string|null), state_changed (bool)
  - If state_changed=true, response includes new_screenshot (base64) OR new_ui_tree_digest (hash)
  - **Current Gap**: Tools return success but no state change evidence

### 2.3 Event Monitoring (Async)

**2.3.1** WHEN an application shows unsolicited UI (toasts, popups), the agent SHALL be notified via event queue
- **Acceptance Criteria**:
  - Agent calls `subscribe_to_events` with event types: ["window_opened", "toast_shown", "dialog_shown"]
  - Events queued internally, not pushed via stdout (MCP is request/response)
  - Next call to `get_ui_tree` injects high-priority notifications at tree root
  - Each event includes: type, timestamp, window_title, auto_dismiss_timeout
  - **Current Gap**: `listen_for_event` placeholder exists but not implemented

### 2.4 Self-Healing & Anchor-Based Navigation

**2.4.1** WHEN an agent's selector fails, it SHALL attempt anchor-based recovery
- **Acceptance Criteria**:
  - Agent calls `find_element_near_anchor` with anchor_text and target_control_type
  - Server searches for static text matching anchor_text
  - Server finds sibling elements of target_control_type within 10px vertical alignment
  - Returns element selector for closest match to anchor's right/below
  - **Current Gap**: Not implemented - agents must handle recovery in prompt logic

**2.4.2** WHEN an agent encounters a dynamic AutomationId, it SHALL use regex/wildcard selectors
- **Acceptance Criteria**:
  - `find_element` accepts `automation_id_pattern` (regex) in addition to exact `automation_id`
  - Example: `automation_id_pattern: "txtField_\d+"` matches txtField_9921, txtField_4421
  - **Current Gap**: Only exact match supported

### 2.5 Progressive Disclosure (Performance)

**2.5.1** WHEN an agent explores a new application, it SHALL start with shallow tree depth
- **Acceptance Criteria**:
  - `get_ui_tree(max_depth=2)` returns top-level window and immediate children only
  - Agent can then call `get_ui_tree(max_depth=5, target_window="Settings Panel")` for specific area
  - **Current Gap**: `list_elements` has `max_depth` but doesn't support targeted expansion

### 2.6 Process Isolation & Scoping

**2.6.1** WHEN an agent attaches to a process, ALL subsequent operations SHALL default to windows owned by that PID
- **Acceptance Criteria**:
  - `attach_to_process(pid)` or `launch_app()` sets session-level `active_pid` context
  - `get_ui_tree()` with no PID argument only scans windows where `window.ProcessId == active_pid`
  - Agent must explicitly pass `filter_by_pid: null` to scan entire desktop (rare case)
  - Performance: Skip desktop-wide search, use cached process window list
  - **Current Gap**: `GetWindowByTitle()` scans entire desktop, no PID filtering
  - **Rationale**: Prevent accidental cross-app interactions (clicking Slack when exploring Notepad)

**2.6.2** WHEN an application spawns a dialog in a different process, the agent SHALL be notified
- **Acceptance Criteria**:
  - `get_ui_tree()` detects when top window PID != active_pid
  - Returns warning: `"foreign_process_detected": {"pid": 8821, "name": "splwow64.exe", "reason": "System dialog"}`
  - Agent can call `attach_to_process(8821, temporary=true)` to interact with dialog
  - After dialog closes, session reverts to original PID
  - **Current Gap**: No detection of cross-process dialogs (WoW64, File Open, Print)

### 2.7 Sandboxing & Safety (CRITICAL)

**2.7.0** WHEN autonomous agent mode is enabled, running WITHOUT a sandbox MUST be blocked
- **Acceptance Criteria**:
  - MCP server checks for `REQUIRE_SANDBOX=true` environment variable or config flag
  - If set, `launch_app()` without sandboxing returns error: `"Sandboxing required for agent mode. Use launch_app_sandboxed or disable REQUIRE_SANDBOX"`
  - Manual testing (human-driven) can bypass with explicit override
  - **Rationale**: Prevent "I forgot to sandbox" accidents during development
  - **Current Gap**: No enforcement - developer must remember to use sandbox

**2.7.1** WHEN testing applications, they MUST run in Windows Sandbox with NO access to host filesystem
- **Acceptance Criteria**:
  - MCP server supports `launch_app_sandboxed(app_path, sandbox_config_path)` tool
  - `sandbox_config_path` points to .wsb file (Windows Sandbox configuration)
  - **CRITICAL**: Sandbox VM has NO access to `C:\Users\jhedin\` or any host drives
  - Sandbox sees ONLY: its own isolated `C:\` + explicitly mapped folders
  - Even if app opens File Explorer inside sandbox, it cannot browse host filesystem
  - Sandbox MUST have network disabled by default (prevent real API calls, email sends)
  - Mapped folders are read-only for inputs, write-only for outputs
  - **Attack Vector Blocked**: Agent clicks "Open File" → File Explorer opens → navigates to (non-existent) `C:\Users\jhedin\Documents` → sees nothing, cannot delete real files
  - **Current Gap**: No sandboxing support - agents run apps directly on host OS
  - **Risk**: Agent could delete `C:\Users\jhedin\Documents\important-file.docx` during exploration

**2.7.2** WHEN an agent completes testing, the sandbox SHALL be destroyed (ephemeral environment)
- **Acceptance Criteria**:
  - `close_sandbox(sandbox_id)` terminates Windows Sandbox instance
  - All state inside sandbox is lost (clean slate for next test)
  - Host extracts artifacts (screenshots, logs, test cases) from mapped output folder BEFORE termination
  - **Rationale**: Prevent state accumulation, ensure reproducible test environment

**2.7.3** WHEN an agent needs to test with real files, they SHALL be copied into sandbox via mapped folders ONLY
- **Acceptance Criteria**:
  - .wsb config specifies `<MappedFolder><HostFolder>C:\TestInputs</HostFolder><SandboxFolder>C:\Inputs</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>`
  - Agent can read files from `C:\Inputs` (read-only, scoped to test data only)
  - Agent writes results to `C:\Outputs` (mapped to `C:\TestOutputs` on host)
  - **Mapped folders are the ONLY filesystem boundary crossing**
  - Host directories `C:\TestInputs` and `C:\TestOutputs` MUST be dedicated test folders, NOT `C:\Users\jhedin\Documents` or any production folder
  - **Validation**: MCP server SHOULD warn if mapped host folder is inside `C:\Users\` (common accident)
  - **Current Gap**: No mapped folder abstraction - agent must manually set up .wsb files

**2.7.3a** WHEN loading a .wsb config, MCP server SHALL validate it for safety violations
- **Acceptance Criteria**:
  - Check that `<Networking>` is set to `Disable` (prevent real network calls)
  - Check that no mapped folder has `<HostFolder>` pointing to sensitive locations:
    - `C:\Users\{username}\Documents`
    - `C:\Users\{username}\Desktop`
    - `C:\Users\{username}\Downloads`
    - `C:\Program Files` (except for reading app binaries)
    - Root drives (`C:\`, `D:\`)
  - If violation detected, return error: `"Unsafe .wsb config: Mapped folder exposes sensitive path"`
  - Allow override with explicit flag: `--allow-dangerous-mappings` (requires user acknowledgment)
  - **Rationale**: Prevent developer from accidentally mapping entire Documents folder
  - **Current Gap**: No .wsb validation exists

**2.7.4** WHEN sandboxing is not available (non-Windows, VM guest, limited permissions), the MCP SHALL warn the agent
- **Acceptance Criteria**:
  - `get_capabilities()` returns `"sandbox_available": false` if Windows Sandbox is not enabled
  - `launch_app()` returns warning: `"running_unsandboxed": true, "risk": "Agent has access to real file system"`
  - Agent prompt MUST be configured to ask user for confirmation before interacting with unsandboxed apps
  - **Current Gap**: No capability detection, no safety warnings

**2.7.5** WHEN an agent generates potentially destructive actions, the MCP SHALL require explicit confirmation
- **Acceptance Criteria**:
  - Actions flagged as destructive: `delete_file`, `send_keys("^A{DELETE}")` in text editor, `click_element("Delete All")`
  - If `confirmation_required: true` mode enabled, MCP pauses and returns: `"confirmation_needed": "About to delete file 'report.docx'. Confirm?"`
  - Human or agent supervisor must call `confirm_action(action_id)` to proceed
  - **Current Gap**: No action confirmation system

**2.7.6** Defense-in-Depth: Multiple Security Layers
- **Layer 1 - Filesystem Isolation (PRIMARY)**:
  - Windows Sandbox VM has its own isolated `C:\` drive
  - Host `C:\Users\jhedin\` does NOT exist inside sandbox
  - Even if agent navigates File Explorer to `C:\Users\jhedin\Documents`, it sees sandbox's empty directory, not host files
  - **This blocks the "Open File → Delete Document" attack vector**

- **Layer 2 - PID Scoping (SECONDARY)**:
  - After `attach_to_process(notepad.exe)`, `get_ui_tree()` only returns Notepad windows
  - If File Explorer opens (different PID), agent must explicitly call `attach_to_process(explorer.exe)` to interact
  - Reduces accidental interactions, but does NOT prevent intentional navigation

- **Layer 3 - Action Confirmation (TERTIARY)**:
  - Destructive actions (Delete, Save As, Send Email) trigger confirmation prompts
  - Provides last-resort human-in-loop for manual intervention
  - **Not sufficient alone** - agent could click through thousands of confirmations

- **Layer 4 - Network Isolation**:
  - `<Networking>Disable</Networking>` prevents real API calls, email sends, database writes
  - Even if agent clicks "Send Email", it fails silently (no network)

**Security Model**: Layer 1 (filesystem isolation) is the **primary defense**. Layers 2-4 provide additional safety but are NOT substitutes for proper sandboxing.

**Example Safe .wsb Configuration**:
```xml
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Disable</Networking>  <!-- Layer 4: No network -->
  <MappedFolders>
    <!-- Layer 1: ONLY these folders visible to sandbox -->
    <MappedFolder>
      <HostFolder>C:\AgentTestInputs</HostFolder>  <!-- Dedicated test folder -->
      <SandboxFolder>C:\Inputs</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\AgentTestOutputs</HostFolder>  <!-- Dedicated output folder -->
      <SandboxFolder>C:\Outputs</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>C:\Inputs\mcp-server.exe</Command>
  </LogonCommand>
</Configuration>
```

**Example UNSAFE Configuration** (REJECTED by validation):
```xml
<MappedFolder>
  <HostFolder>C:\Users\jhedin\Documents</HostFolder>  ❌ REJECTED
  <SandboxFolder>C:\Documents</SandboxFolder>
  <ReadOnly>false</ReadOnly>
</MappedFolder>
<!-- Error: "Unsafe .wsb config: Mapped folder exposes sensitive path C:\Users\jhedin\Documents" -->
```

---

## 3. Non-Functional Requirements

### 3.1 Performance

**NFR-1**: `get_ui_tree` SHALL complete in <2 seconds for max_depth=3 on complex apps (e.g., Visual Studio)

**NFR-2**: `get_ui_tree` SHALL apply heuristic pruning to limit token payload to <5000 tokens at depth=3

**NFR-3**: `check_element_state` SHALL complete in <100ms for cached elements

### 3.2 Reliability

**NFR-4**: All tools SHALL handle ElementNotAvailableException and return structured error (not crash)

**NFR-5**: All tools SHALL handle invalid HWND (window closed) and return error: "Window no longer exists"

**NFR-6**: DPI scaling SHALL be normalized: all coordinates converted from logical to physical pixels

### 3.3 Compatibility

**NFR-7**: MCP server SHALL work with FlaUI UIA2 backend (no visual requirements)

**NFR-8**: MCP server SHALL support legacy WinForms, WPF, and Win32 applications

### 3.4 Observability

**NFR-9**: All tool responses SHALL include execution_time_ms for performance debugging

**NFR-10**: Server SHALL log (to stderr) when tree pruning reduces element count >90%

---

## 4. Edge Cases

### 4.1 DPI Scaling Mismatch

**Problem**: Agent requests click at logical coordinates (500, 300) but screen is 150% DPI
- **Expected**: Server normalizes to physical coordinates (750, 450) before click
- **Current**: Unknown if normalization happens - needs verification

### 4.2 Window Focus Stealing Prevention

**Problem**: Agent calls `type_text` on background window, OS blocks focus change
- **Expected**: Server attempts `set_focus()`, verifies foreground window PID matches, retries with minimize-restore cycle
- **Current**: Unknown if retry logic exists

### 4.3 Virtualized List Exploration

**Problem**: Agent calls `get_ui_tree` on File Explorer with 5000 files, only 20 visible
- **Expected**: Tree shows 20 visible items + ScrollPattern indicator. Agent calls `scroll(direction=down)` to reveal more
- **Current**: `list_elements` would show 20 items but no scroll tool exists

### 4.4 Dynamic AutomationId Suffix

**Problem**: Element AutomationId changes from `btn_Submit_123` to `btn_Submit_456` on app restart
- **Expected**: Agent uses `automation_id_pattern="btn_Submit_\d+"` or falls back to Name="Submit"
- **Current**: Exact match only - agent must handle pattern matching in prompt

### 4.5 Event Queue Overflow

**Problem**: App shows 50 toast notifications while agent is processing
- **Expected**: Event queue capped at 10, oldest discarded, warning injected into next tree response
- **Current**: No event queue - agent cannot detect unsolicited UI

### 4.6 Dirty Tree Performance (Legacy Apps)

**Problem**: SAP GUI tree returns 100,000 UIA elements (50MB XML), exceeds MCP message limits
- **Expected**: Server applies aggressive pruning, limits to max_depth=2 by default, warns agent of truncation
- **Current**: Unknown if pruning is aggressive enough

### 4.7 Cross-Process File Access Attack (CRITICAL SECURITY)

**Problem**: Agent explores text editor → clicks "Open File" → File Explorer opens (new PID) → agent navigates to `C:\Users\jhedin\Documents` → right-clicks `important-file.docx` → Delete
- **Layer 1 Defense (PRIMARY)**: Sandbox isolation - host `C:\Users\jhedin\Documents` does NOT exist inside sandbox VM. Agent sees empty directory, cannot delete real files.
- **Layer 2 Defense (SECONDARY)**: After `attach_to_process(notepad.exe)`, `get_ui_tree()` only returns Notepad windows. When File Explorer opens, agent must explicitly call `attach_to_process(explorer.exe)` to interact. Reduces accidental clicks but does NOT prevent intentional navigation.
- **Layer 3 Defense (TERTIARY)**: Destructive actions trigger confirmation: `"confirmation_needed": "About to delete file 'important-file.docx'. Confirm?"`
- **Failure Mode if no sandbox**: Agent deletes real production file. **UNACCEPTABLE**.
- **Current**: No sandboxing, no defense layers → **critical security gap**

### 4.8 Accidental Sensitive Folder Mapping

**Problem**: Developer creates .wsb file, accidentally maps `C:\Users\jhedin\Documents` as writable
- **Expected**: MCP server validates .wsb config, rejects with error: `"Unsafe .wsb config: Mapped folder exposes sensitive path"`
- **Workaround**: Developer uses dedicated test folders `C:\AgentTestInputs` and `C:\AgentTestOutputs`
- **Current**: No .wsb validation → developer mistake = data loss

### 4.9 Network Isolation Bypass

**Problem**: Agent explores email client → clicks "Send" → real email sent to customer
- **Expected**: Sandbox has `<Networking>Disable</Networking>` → email send fails silently, no network access
- **Verification**: MCP server validates .wsb has networking disabled
- **Current**: No network isolation enforcement

---

## 5. Out of Scope

The following are explicitly OUT OF SCOPE for this specification:

1. **Visual AI (OmniParser/OCR)**: Coordinate-based clicking via bounding boxes overlaid on screenshots - agents should use structural selectors (AutomationId, Name) not pixel analysis
2. **COM Automation**: Direct API calls to Office applications (Excel.Application.Workbooks.Open) - bypass GUI automation entirely
3. **Test Case Execution**: Running generated Playwright/PyWinAuto scripts - generation is in scope, execution is not
4. **Multi-Application Orchestration**: Coordinating actions across multiple running apps simultaneously - focus on single-app exploration with PID isolation
5. **Input Recording**: Capturing human interactions as replay logs - focus on agent-driven exploration
6. **Automatic .wsb Generation**: MCP won't auto-generate sandbox configs - user provides pre-configured .wsb files

---

## 6. Tool Comparison Matrix

| Tool | Current Status | Required | Priority | Gap Description |
|------|---------------|----------|----------|-----------------|
| `get_ui_tree` | ❌ Missing | ✅ Yes | P0 | Have `list_elements` but not hierarchical with pruning |
| `expand_collapse` | ❌ Missing | ✅ Yes | P0 | Cannot explore nested menus/trees |
| `scroll` | ❌ Missing | ✅ Yes | P0 | Cannot explore virtualized lists |
| `check_element_state` | ⚠️ Partial | ✅ Yes | P0 | Have `element_exists` + `get_property`, need unified call |
| `get_process_info` | ❌ Missing | ✅ Yes | P1 | Cannot track process continuity for focus management |
| `subscribe_to_events` | ⚠️ Placeholder | ✅ Yes | P1 | `listen_for_event` exists but not implemented |
| `find_element_near_anchor` | ❌ Missing | ⚠️ Nice-to-have | P2 | Self-healing can be done in prompt logic |
| `automation_id_pattern` | ❌ Missing | ⚠️ Nice-to-have | P2 | Regex selectors for dynamic IDs |
| `get_screenshot` | ✅ Implemented | ✅ Yes | ✅ | `take_screenshot` exists |
| `type_text` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `click_element` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `wait_for_element` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `find_element` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `launch_app` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `attach_to_process` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `focus_window` | ✅ Implemented | ✅ Yes | ✅ | Exists |
| `drag_drop` | ✅ Implemented | ⚠️ Nice-to-have | ✅ | Advanced interaction |
| `mouse_click` | ✅ Implemented | ⚠️ Fallback | ✅ | Coordinate-based last resort |
| `touch_*` / `pen_*` | ✅ Implemented | ❌ No | ✅ | Specialized, not core to agents |
| **SANDBOXING** |  |  |  |  |
| `launch_app_sandboxed` | ❌ Missing | ✅ Yes | **P0** | Safety-critical - prevent data loss during testing |
| `close_sandbox` | ❌ Missing | ✅ Yes | **P0** | Clean up ephemeral test environments |
| `get_capabilities` | ❌ Missing | ✅ Yes | P1 | Detect if sandboxing is available |
| `confirm_action` | ❌ Missing | ⚠️ Nice-to-have | P2 | Human-in-loop for destructive actions |
| **PROCESS SCOPING** |  |  |  |  |
| PID filtering in `get_ui_tree` | ❌ Missing | ✅ Yes | **P0** | Prevent cross-app interaction accidents |
| Session-level `active_pid` | ❌ Missing | ✅ Yes | **P0** | Default scope after attach_to_process |
| Foreign process detection | ❌ Missing | ✅ Yes | P1 | Handle WoW64 dialogs (File Open, Print) |

**Priority Legend**:
- **P0**: Blocking - agents cannot function without this
- **P1**: Important - agents can work around but significantly degraded
- **P2**: Nice-to-have - improves agent reliability but not critical

---

## 7. Success Criteria

This feature is successful when:

**Core Functionality:**
1. ✅ An autonomous agent can explore a WinForms application (e.g., Calculator) and generate a test case covering 3+ user flows
2. ✅ An agent can detect a bug (e.g., button click that causes no state change) and report it with reproduction steps
3. ✅ An agent can identify a UX gap (e.g., missing "Clear" button on a form) by comparing against CRUD heuristics
4. ✅ `get_ui_tree` token payload is <5000 tokens for depth=3 on complex apps (Visual Studio, SAP GUI)
5. ✅ Agent can explore a nested menu (File > Settings > Advanced) using `expand_collapse`
6. ✅ Agent can scroll through a large list (1000+ items) using `scroll` and find target item
7. ✅ Agent receives notification when an unexpected dialog appears via event queue integration

**Safety & Isolation:**
8. ✅ Agent can launch Notepad in Windows Sandbox, type text, save file to mapped output folder, and close sandbox without affecting host
9. ✅ After `attach_to_process(notepad.exe)`, agent calls `get_ui_tree()` and ONLY sees Notepad windows (not Chrome, Slack, etc.)
10. ✅ Agent exploring a text editor accidentally clicks "Delete All" → MCP pauses and returns confirmation request before executing
11. ✅ On system without Windows Sandbox, `get_capabilities()` returns warning and agent prompts user for confirmation before continuing

**Failure Criteria (UNACCEPTABLE)**:
- ❌ Agent gets stuck in loop because it cannot detect when a button click did nothing (no state change feedback)
- ❌ Agent hallucinates elements because `get_ui_tree` returned truncated/incomplete tree without warning
- ❌ Agent cannot explore menus because `expand_collapse` is missing
- ❌ MCP server crashes or hangs when `get_ui_tree` is called on legacy app with 50k+ elements

**Security Failures (CRITICAL - MUST NOT HAPPEN)**:
- ❌ **Agent explores Notepad → clicks "Open File" → File Explorer opens → agent navigates to `C:\Users\jhedin\Documents` → deletes `important-file.docx` → real file lost**
  - Root Cause: No sandbox isolation, host filesystem accessible
  - Impact: CATASTROPHIC - data loss
- ❌ **Agent accidentally interacts with Slack/Email while exploring Calculator**
  - Root Cause: No PID scoping, desktop-wide window search
  - Impact: HIGH - accidental messages sent, workspace deleted
- ❌ **Agent explores email client → clicks "Send" → real email sent to customer**
  - Root Cause: No network isolation in sandbox
  - Impact: HIGH - embarrassing/harmful external communication
- ❌ **Developer accidentally maps `C:\Users\jhedin\Documents` in .wsb config → agent testing deletes production files**
  - Root Cause: No .wsb validation, sensitive path allowed
  - Impact: CATASTROPHIC - data loss due to developer mistake
