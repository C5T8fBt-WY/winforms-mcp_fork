# MCP Tool Coverage for Autonomous Agent Support - Requirements v2.0

## 1. Introduction

This specification defines the required MCP tool coverage for the Rhombus.WinFormsMcp server to support autonomous UI exploration agents. The goal is to enable LLM agents to explore Windows applications, identify bugs, generate test cases, analyze UX gaps, and produce comprehensive documentation without human intervention.

**Current State**: The WinForms MCP has 26 tools focused on direct UI manipulation (clicks, text input, screenshots) but lacks critical tools for autonomous agent workflows like hierarchical tree inspection, progressive disclosure, and event monitoring.

**Target State**: A complete MCP tool suite that matches the capabilities described in battle-tested agent frameworks (Anthropic Computer Use, Stagehand, Skyvern, Microsoft UFO) and supports the five analysis lenses (test generation, bug detection, UX analysis, complexity calculation, documentation).

**Architecture Decision** (based on research spike): Windows Sandbox with MCP server running inside sandbox provides kernel-level isolation while supporting full GUI automation. See `research-sandbox-architecture.md` for detailed analysis.

**Background Automation** (based on research spike): Agents can run in background while user continues normal laptop use. See `research-background-automation.md` for three-layer architecture (VDD + Registry + InjectTouchInput).

---

## 1.1 Host Prerequisites

Before using the MCP server with Windows Sandbox background automation, the host machine requires:

**Windows Edition** (REQUIRED):
- Windows 10/11 **Pro**, **Enterprise**, or **Education**
- Windows Home editions do **NOT** support Windows Sandbox
- Check your edition: `winver` or `Settings → System → About`

**Registry Key** (one-time setup, Administrator required):
```powershell
# Prevents RDP session throttling when sandbox window is minimized
$registryPath = "HKLM:\Software\Microsoft\Terminal Server Client"
New-ItemProperty -Path $registryPath -Name "RemoteDesktop_SuppressWhenMinimized" -Value 2 -PropertyType DWORD -Force
```

**Why**: Windows Sandbox uses RDP internally. By default, minimizing the sandbox window causes the RDP client to signal the guest to stop rendering, breaking automation. This registry key prevents that behavior.

**Virtual Display Driver (VDD)**: Bundled with MCP server, installed automatically inside sandbox during bootstrap. Provides a virtual monitor target so Desktop Window Manager (DWM) continues rendering even when no physical display is connected.

---

## 1.3 Terminology

- **Agent**: Autonomous LLM performing UI exploration (e.g., Claude Computer Use, GPT-4 Vision)
- **Tool**: MCP-exposed function callable via JSON-RPC (e.g., `get_ui_tree`, `click_element`)
- **Element**: Windows UI Automation (UIA) element (button, textbox, menu, etc.)
- **Session**: Stateful context maintained by MCP server (active PID, cached elements, event queue)
- **Sandbox**: Windows Sandbox VM instance with ephemeral filesystem isolation
- **Parameter Naming**: snake_case for tool parameters, PascalCase for UIA properties
- **Complex Application**: Application with ≥500 UI elements OR tree depth ≥5 levels

---

## 2. User Stories

### 2.1 Core Observation & Navigation

**User Story 2.1**: As an autonomous testing agent, I want to inspect the UI hierarchy at configurable depths, so that I can understand the application structure without overwhelming my context window with unnecessary details.

**2.1.1** WHEN the agent requests the current UI state, the system SHALL provide a `get_ui_tree` tool that returns hierarchical UIA trees
- **Acceptance Criteria**:
  - The tool SHALL return a hierarchical UIA tree in JSON format
  - The tool SHALL accept a `max_depth` parameter (type: integer, default: 3, valid range: 1-10)
  - WHEN `max_depth` is outside valid range (1-10), the tool SHALL return error "max_depth must be between 1 and 10"
  - The tool SHALL accept an `include_offscreen` parameter (type: boolean, default: false)
  - WHEN `include_offscreen=false`, the tool SHALL exclude elements with `IsOffscreen=true`
  - The tool SHALL apply heuristic pruning: remove ControlType IN [Pane, Group, Image] that have zero interactive children
  - For each element, the tool SHALL return: AutomationId (string), Name (string), ControlType (string), BoundingRectangle ([x, y, w, h]), IsEnabled (bool)
  - WHEN `max_depth=3` on a complex application (≥500 elements), the tree payload SHALL NOT exceed 5000 tokens
  - WHEN tree exceeds token budget after pruning, the tool SHALL reduce depth by 1 and retry until payload ≤5000 tokens OR depth=1
  - The response SHALL include: `token_count` (actual), `element_count` (before pruning), `pruned_count`, `max_depth_reached` (bool)

- **Implementation Notes**:
  - Current: `list_elements` returns flat list only
  - Required: Recursive tree traversal with depth limiting and token budget enforcement

**2.1.2** WHEN the agent encounters a collapsed container (menu, tree node, accordion), the system SHALL provide an `expand_collapse` tool to reveal hidden children
- **Acceptance Criteria**:
  - The tool SHALL accept `selector_type` enum: ["automation_id", "name"]
  - The tool SHALL accept `selector_value` (type: string)
  - The tool SHALL accept `expand` (type: boolean, true=expand, false=collapse)
  - WHEN element is found, the tool SHALL invoke UIA ExpandCollapsePattern.Expand() or Collapse()
  - The tool SHALL wait for StructureChangedEvent OR 500ms timeout, whichever occurs first
  - WHEN timeout occurs without StructureChangedEvent, the tool SHALL return warning "Timeout waiting for expand, children may still be loading"
  - The response SHALL include: `success` (bool), `children_revealed` (integer), `current_state` enum: ["Expanded", "Collapsed", "PartiallyExpanded"]

- **Implementation Notes**:
  - Current: Not implemented
  - Required: FlaUI ExpandCollapsePattern integration with event monitoring

**2.1.3** WHEN the agent needs to navigate virtualized lists (>100 items), the system SHALL provide a `scroll` tool to reveal off-screen content
- **Acceptance Criteria**:
  - The tool SHALL accept `selector_type` enum: ["automation_id", "name"]
  - The tool SHALL accept `selector_value` (type: string) identifying the scrollable container
  - The tool SHALL accept `direction` enum: ["up", "down", "left", "right"]
  - The tool SHALL accept `amount` enum: ["line", "page", "end"]
  - WHEN container does not support ScrollPattern, the tool SHALL return error "Element is not scrollable"
  - The tool SHALL invoke ScrollPattern.Scroll(direction, amount)
  - The response SHALL include: `success` (bool), `scroll_percent` (float 0-100), `at_boundary` (bool), `new_visible_items` (array of Name or AutomationId strings)
  - WHEN `new_visible_items` is empty AND `at_boundary=true`, agent SHALL interpret as "reached end of list"

- **Implementation Notes**:
  - Current: Not implemented
  - Required: FlaUI ScrollPattern integration with before/after element snapshot comparison

---

### 2.2 State Validation & Synchronization

**User Story 2.2**: As an autonomous bug detection agent, I want to validate element states and detect when actions cause no state change, so that I can identify broken interactions and report bugs with reproduction steps.

**2.2.1** WHEN the agent needs to verify element state before interaction, the system SHALL provide a `check_element_state` tool
- **Acceptance Criteria**:
  - The tool SHALL accept `selector_type` enum: ["automation_id", "name", "cached_id"]
  - The tool SHALL accept `selector_value` (type: string)
  - The response SHALL include: `exists` (bool), `is_enabled` (bool), `is_visible` (bool), `is_focused` (bool)
  - The response SHALL include: `bounding_rect` (array: [x, y, width, height] in logical coordinates)
  - The response SHALL include: `control_type` (string), `value` (string | null for Value pattern)
  - WHEN `selector_type="cached_id"`, the tool SHALL retrieve element from session cache (target: <50ms latency)
  - WHEN `selector_type="cached_id"` AND element not in cache, the tool SHALL return error "Element cache miss: <selector_value>"
  - WHEN `selector_type="cached_id"` AND element is stale, the tool SHALL attempt self-healing (re-find by AutomationId) before returning error

- **Implementation Notes**:
  - Current: `element_exists` and `get_property` exist but not unified
  - Required: Unified state query with caching fast-path

**2.2.2** WHEN the agent executes an interaction tool (click, type), the system SHALL detect state changes and report them
- **Acceptance Criteria**:
  - All interaction tools (click_element, type_text, etc.) SHALL include in their response: `success` (bool), `state_changed` (bool), `state_diff_summary` (string | null)
  - The system SHALL compute state changes via the following algorithm:
    1. Before action: Compute UI tree structural hash (include: AutomationId, Name, ControlType, IsEnabled, IsVisible for all elements in target window)
    2. Execute interaction
    3. Wait up to 500ms for StructureChangedEvent OR PropertyChangedEvent, whichever occurs first
    4. After action: Recompute UI tree structural hash
    5. Compare hashes: `state_changed = (before_hash != after_hash)`
    6. WHEN `state_changed=true`, generate diff summary describing what changed (e.g., "Button 'Submit' disabled, Dialog 'Confirmation' appeared")
  - WHEN state change occurs in different process (dialog spawn), the response SHALL include warning: `foreign_process_detected` with PID and process name
  - Hash computation SHALL complete in <50ms
  - False positive rate (cosmetic-only changes like hover effects) SHALL be <5%

- **Implementation Notes**:
  - Current: Tools return success but no state change evidence
  - Required: Tree hashing algorithm with event monitoring

**2.2.3** WHEN the agent needs process information for window targeting, the system SHALL provide a `get_process_info` tool
- **Acceptance Criteria**:
  - The tool SHALL accept `window_handle` (type: string, format: "0xHHHHHHHH" hex HWND)
  - The response SHALL include: `pid` (integer), `process_name` (string), `is_responding` (bool), `window_state` enum: ["normal", "minimized", "maximized"]
  - The tool SHALL use Win32 GetWindowThreadProcessId() to retrieve PID
  - WHEN window_handle is invalid, the tool SHALL return error "Invalid window handle"
  - Execution time SHALL be <50ms

- **Implementation Notes**:
  - Current: Not implemented
  - Required: Win32 API integration for PID retrieval

---

### 2.3 Event Monitoring (Async)

**User Story 2.3**: As an autonomous agent exploring complex applications, I want to be notified of unsolicited UI changes (toasts, popups, dialogs), so that I can handle unexpected state transitions and avoid getting stuck.

**2.3.1** WHEN the application shows unsolicited UI (toasts, popups), the agent SHALL be notified via event queue integration
- **Acceptance Criteria**:
  - The system SHALL provide a `subscribe_to_events` tool
  - The tool SHALL accept `event_types` (array of enum: ["window_opened", "toast_shown", "dialog_shown", "structure_changed", "property_changed"])
  - The system SHALL maintain an internal event queue (max size: 10 events, FIFO eviction)
  - Events SHALL be filtered by session's `active_pid` (only events from target process)
  - WHEN queue exceeds 10 events, the system SHALL evict oldest event and set `events_dropped` flag
  - Events SHALL NOT be pushed via stdout (MCP is request/response, not streaming)
  - The next `get_ui_tree()` call SHALL inject high-priority notifications at tree root under `<Notifications>` element
  - Each event SHALL include: `type` (string), `timestamp` (ISO 8601), `window_title` (string), `auto_dismiss_timeout` (integer ms | null)
  - WHEN `events_dropped=true`, the response SHALL include warning "Event queue overflowed, <N> events lost"

- **Implementation Notes**:
  - Current: `listen_for_event` placeholder exists but not implemented
  - Required: FlaUI automation event registration with queue management

---

### 2.4 Self-Healing & Anchor-Based Navigation

**User Story 2.4**: As an autonomous agent working with dynamic UIs, I want to recover from selector failures using anchor-based navigation, so that I can continue exploration even when AutomationIds change between runs.

**2.4.1** WHEN an agent's selector fails, the system SHALL provide recovery suggestions
- **Acceptance Criteria**:
  - WHEN tool returns error "Element not found", the response SHALL include structured `recovery_suggestions` array
  - Each suggestion SHALL include: `action` (string), `target` (string | null), `parameters` (object), `reason` (string)
  - Example suggestion for offscreen element:
    ```json
    {
      "action": "scroll",
      "target": "MainPanel",
      "parameters": {"direction": "down", "amount": "page"},
      "reason": "Element may be in scrollable container"
    }
    ```
  - WHEN AutomationId search fails, suggestions SHALL include: "Try name-based selector" with specific `parameters: {"selector_type": "name", "selector_value": "<inferred_name>"}`

- **Implementation Notes**:
  - Current: Error messages lack actionable context
  - Required: Intelligent error analysis with context-aware suggestions

**2.4.2** WHEN an agent encounters dynamic AutomationIds (suffixes change), the system SHALL support pattern-based selectors
- **Acceptance Criteria**:
  - The `find_element` tool SHALL accept `automation_id_pattern` (type: string, regex) in addition to exact `automation_id`
  - WHEN `automation_id_pattern` is provided, the system SHALL use regex matching (e.g., `"btn_Submit_\d+"` matches `btn_Submit_9921`)
  - WHEN multiple elements match pattern, the tool SHALL return error "Multiple elements match pattern" with count
  - WHEN no elements match pattern, the tool SHALL fall back to name-based search if `name` parameter also provided

- **Implementation Notes**:
  - Current: Only exact match supported
  - Required: Regex matching capability in element discovery

---

### 2.5 Progressive Disclosure (Performance)

**User Story 2.5**: As an autonomous agent with token budget constraints, I want to explore UIs progressively (shallow first, then deep), so that I can efficiently navigate complex applications without exceeding context window limits.

**2.5.1** WHEN the agent explores a new application, the system SHALL support shallow tree inspection
- **Acceptance Criteria**:
  - `get_ui_tree(max_depth=1)` SHALL return only top-level window and immediate children
  - `get_ui_tree(max_depth=2)` SHALL return window, children, and grandchildren only
  - The agent MAY call `get_ui_tree(max_depth=5, target_window="Settings Panel")` to expand specific area
  - WHEN `target_window` is specified, the system SHALL use it as root for depth counting (not desktop root)
  - Token budget enforcement (5000 tokens) SHALL apply after selecting target window

- **Implementation Notes**:
  - Current: `list_elements` has `max_depth` but doesn't support targeted expansion
  - Required: Parameterized tree root selection

---

### 2.6 Process Isolation & Scoping

**User Story 2.6**: As an autonomous agent testing multiple applications concurrently, I want session-level process scoping, so that I interact only with my target application and don't accidentally affect other running programs.

**2.6.1** WHEN the agent attaches to a process, ALL subsequent operations SHALL default to windows owned by that PID
- **Acceptance Criteria**:
  - `attach_to_process(pid)` OR `launch_app()` SHALL set session-level `active_pid` context
  - `get_ui_tree()` with no explicit PID parameter SHALL scan ONLY windows where `window.ProcessId == active_pid`
  - The agent MAY explicitly pass `filter_by_pid: null` to scan entire desktop (escape hatch for cross-process scenarios)
  - WHEN `active_pid` is set, execution time for `get_ui_tree()` SHALL be <500ms (vs. 2s baseline for desktop-wide search)
  - WHEN `active_pid` is set, element cache SHALL track PIDs and reject cached elements from different processes

- **Implementation Notes**:
  - Current: `GetWindowByTitle()` scans entire desktop, no PID filtering
  - Required: Session-level PID context with window enumeration optimization

**2.6.2** WHEN an application spawns a dialog in different process, the agent SHALL be notified
- **Acceptance Criteria**:
  - `get_ui_tree()` SHALL detect when foreground window PID != `active_pid`
  - The response SHALL include warning:
    ```json
    {
      "warning": {
        "type": "foreign_process_detected",
        "pid": 8821,
        "process_name": "splwow64.exe",
        "reason": "System dialog (Print, File Open, UAC)"
      }
    }
    ```
  - The agent MAY call `attach_to_process(8821, temporary=true)` to interact with foreign dialog
  - WHEN `temporary=true` AND foreign process window closes, the system SHALL automatically revert to original `active_pid`
  - WHEN agent forgets to revert manually, the system SHALL revert after 30s timeout with warning

- **Implementation Notes**:
  - Current: No detection of cross-process dialogs
  - Required: Foreground window PID monitoring with automatic session reversion

---

### 2.7 Sandboxing & Safety (CRITICAL)

**User Story 2.7**: As a developer deploying autonomous agents, I need kernel-level filesystem isolation, so that agents can safely explore applications without risk of accidentally deleting production files or corrupting system configuration.

**Architecture Decision** (based on `research-sandbox-architecture.md`):
- Windows Sandbox provides **Tier 1 (High-Risk)** isolation using hypervisor-based virtualization
- MCP server runs **inside the sandbox** for full GUI support
- Host filesystem is isolated via **read-only mapped folders** enforced at kernel level
- All modifications inside sandbox are discarded on termination (ephemeral environment)

**2.7.0** WHEN autonomous agent mode is enabled, running WITHOUT a sandbox SHALL be blocked
- **Acceptance Criteria**:
  - The system SHALL check for environment variable `REQUIRE_SANDBOX=true` OR config flag `sandbox_required: true`
  - WHEN `REQUIRE_SANDBOX=true`, `launch_app()` without sandboxing SHALL return error "Sandboxing required for agent mode. Use launch_app_sandboxed or disable REQUIRE_SANDBOX"
  - Manual testing (human-driven) MAY bypass with explicit flag `--allow-unsandboxed` (requires user confirmation)

- **Implementation Notes**:
  - Current: No enforcement
  - Required: Startup validation and error handling

**2.7.1** WHEN testing applications, they SHALL run in Windows Sandbox with NO access to host filesystem
- **Acceptance Criteria**:
  - The system SHALL provide `launch_app_sandboxed(app_path, sandbox_config_path)` tool
  - The tool SHALL accept `sandbox_config_path` (type: string, absolute path to .wsb file on host)
  - The .wsb file SHALL define sandbox configuration using Windows Sandbox XML schema
  - The sandbox VM SHALL have its own isolated `C:\` drive with NO access to host `C:\Users\<username>\`
  - WHEN agent inside sandbox navigates File Explorer to `C:\Users\jhedin\Documents`, it SHALL see sandbox's empty directory (not host files)
  - The sandbox SHALL have `<Networking>Disable</Networking>` by default (prevent real API calls, email sends)
  - The .wsb SHALL define `<MappedFolders>` for input (read-only) and output (write-only) data exchange
  - Example safe .wsb configuration:
    ```xml
    <Configuration>
      <VGpu>Disable</VGpu>
      <Networking>Disable</Networking>
      <MappedFolders>
        <MappedFolder>
          <HostFolder>C:\AgentTestInputs</HostFolder>
          <SandboxFolder>C:\Inputs</SandboxFolder>
          <ReadOnly>true</ReadOnly>
        </MappedFolder>
        <MappedFolder>
          <HostFolder>C:\AgentTestOutputs</HostFolder>
          <SandboxFolder>C:\Outputs</SandboxFolder>
          <ReadOnly>false</ReadOnly>
        </MappedFolder>
      </MappedFolders>
      <LogonCommand>
        <Command>C:\Inputs\mcp-server.exe</Command>
      </LogonCommand>
    </Configuration>
    ```

- **Implementation Notes**:
  - Current: No sandboxing support
  - Required: Windows Sandbox API integration, .wsb XML parsing, MCP server bootstrap via LogonCommand

**2.7.2** WHEN an agent completes testing, the sandbox SHALL be destroyed (ephemeral environment)
- **Acceptance Criteria**:
  - The system SHALL provide `close_sandbox(sandbox_id)` tool
  - The tool SHALL terminate Windows Sandbox VM process
  - All state inside sandbox (registry, filesystem, temp files) SHALL be lost (no persistence)
  - The system SHALL extract artifacts from `<MappedFolder ReadOnly=false>` BEFORE termination
  - The response SHALL include: `success` (bool), `artifacts_extracted` (array of file paths), `output_folder` (string path on host)

- **Implementation Notes**:
  - Current: No sandbox lifecycle management
  - Required: Process termination with artifact harvesting

**2.7.3** WHEN the system loads a .wsb config, it SHALL validate for safety violations
- **Acceptance Criteria**:
  - The system SHALL parse .wsb XML and validate:
    1. `<Networking>` is set to `Disable` (if not, return error "Networking must be disabled for agent testing")
    2. No `<MappedFolder>` has `<HostFolder>` pointing to sensitive locations:
       - `C:\Users\{username}\Documents`
       - `C:\Users\{username}\Desktop`
       - `C:\Users\{username}\Downloads`
       - `C:\Users\{username}\AppData`
       - `C:\Windows`
       - `C:\Program Files` (except for read-only app binaries)
       - Root drives (`C:\`, `D:\`)
    3. All paths are resolved to canonical form (follows symlinks, normalizes case) before validation
  - WHEN sensitive path detected, the tool SHALL return error "Unsafe .wsb config: Mapped folder exposes sensitive path <canonical_path>"
  - WHEN validation passes, the response SHALL include: `valid: true`, `warnings: []` (e.g., "vGPU enabled increases attack surface")
  - The agent MAY override validation with flag `--allow-dangerous-mappings` (requires explicit user acknowledgment in logs)

- **Implementation Notes**:
  - Current: No .wsb validation
  - Required: XML parsing, path canonicalization, sensitive location database

**2.7.4** WHEN sandboxing is not available (non-Windows, VM guest, disabled feature), the system SHALL warn
- **Acceptance Criteria**:
  - The system SHALL provide `get_capabilities()` tool
  - The response SHALL include: `sandbox_available` (bool)
  - `sandbox_available=false` WHEN:
    - OS is not Windows 10 Pro/Enterprise/Education (1903+) or Windows 11
    - Windows Sandbox feature is not enabled (DISM check)
    - Running inside a VM guest (nested virtualization unsupported)
  - WHEN `sandbox_available=false`, `launch_app()` SHALL return warning: `{"running_unsandboxed": true, "risk": "Agent has access to real file system"}`
  - Agent system prompt SHOULD be configured to request user confirmation before interacting with unsandboxed apps

- **Implementation Notes**:
  - Current: No capability detection
  - Required: OS version check, DISM feature enumeration, VM detection

**2.7.5** Defense-in-Depth: Multiple Security Layers
- **Layer 1 - Filesystem Isolation (PRIMARY)**:
  - Windows Sandbox VM has isolated `C:\` drive (Copy-on-Write, ephemeral)
  - Host `C:\Users\jhedin\` does NOT exist inside sandbox
  - Blocks "Open File → File Explorer → Delete Document" attack vector
  - **Implementation**: Kernel-level isolation by Windows Sandbox architecture

- **Layer 2 - PID Scoping (SECONDARY)**:
  - After `attach_to_process(notepad.exe)`, `get_ui_tree()` only returns Notepad windows
  - If File Explorer opens (different PID), agent must explicitly switch context
  - Reduces accidental interactions but does NOT prevent intentional navigation
  - **Implementation**: Requirement 2.6.1

- **Layer 3 - Network Isolation**:
  - `<Networking>Disable</Networking>` prevents real API calls, email sends, database writes
  - Even if agent clicks "Send Email", operation fails (no network)
  - **Implementation**: .wsb validation (Requirement 2.7.3)

- **Layer 4 - Action Confirmation (TERTIARY)**:
  - Destructive actions (delete files, send emails) trigger confirmation prompts
  - Provides last-resort human-in-loop intervention
  - **NOT sufficient alone** - agent could click through thousands of confirmations
  - **Implementation**: Optional feature (P2 priority)

**Security Model**: Layer 1 (filesystem isolation) is the **PRIMARY defense**. Layers 2-4 provide additional safety but are NOT substitutes for proper sandboxing.

**2.7.6** WHEN the user wants to use their laptop while agents run, the sandbox SHALL support background operation
- **Acceptance Criteria**:
  - The sandbox bootstrap SHALL install a Virtual Display Driver (VDD) inside the sandbox
  - The VDD SHALL provide a virtual monitor target so DWM continues rendering when sandbox window is minimized
  - The host registry key `RemoteDesktop_SuppressWhenMinimized = 2` SHALL prevent RDP session throttling
  - `InjectTouchInput` SHALL be used for click/drag operations (more reliable than mouse injection in headless state)
  - The user SHALL be able to minimize the sandbox window and continue using their laptop normally
  - Automation SHALL continue functioning in the background with <100ms additional latency compared to foreground operation
  - The system SHALL detect if VDD installation failed and return warning: `{"vdd_installed": false, "risk": "Background automation may fail when minimized"}`

- **Three-Layer Architecture**:
  - **Layer 1 - Display**: Indirect Display Driver (IDD/VDD) keeps DWM rendering active
  - **Layer 2 - Session**: Host registry key prevents RDP client from throttling guest
  - **Layer 3 - Input**: `InjectTouchInput` API works reliably in headless state (unlike SendInput for mouse)

- **Implementation Notes**:
  - VDD driver: Use open-source IddSampleDriver or similar UMDF-based virtual display
  - Driver installation: `pnputil /add-driver vdd_driver.inf /install` in LogonCommand
  - Certificate: Driver must be signed or sandbox must import test certificate to TrustedRoot
  - See `research-background-automation.md` for detailed implementation guidance

---

### 2.8 Standard Error Response Format

**User Story 2.8**: As an autonomous agent handling errors, I want structured error responses with actionable context, so that I can implement intelligent recovery strategies instead of blindly retrying.

**2.8.1** WHEN any tool encounters an error, it SHALL return a structured error response
- **Acceptance Criteria**:
  - All tool responses SHALL include: `success` (bool)
  - WHEN `success=false`, the response SHALL include:
    - `error_code` (string enum, see below)
    - `error_message` (string, human-readable description)
    - `recovery_suggestions` (array of structured suggestions, see 2.4.1)
    - `execution_time_ms` (integer)
  - Standard error codes SHALL include:
    - `ELEMENT_NOT_FOUND`: Element selector did not match any elements
    - `ELEMENT_NOT_AVAILABLE`: Element exists but is not currently available (stale reference)
    - `TIMEOUT`: Operation exceeded time limit
    - `INVALID_PARAMETER`: Tool parameter validation failed
    - `PROCESS_TERMINATED`: Target process no longer running
    - `PERMISSION_DENIED`: Insufficient privileges for operation
    - `SANDBOX_NOT_AVAILABLE`: Windows Sandbox feature not enabled
    - `UNSAFE_CONFIG`: .wsb configuration validation failed
  - Error messages SHALL NOT contain:
    - Stack traces (security risk)
    - Internal file paths (information disclosure)
    - Sensitive data (PIDs of other processes OK, but not usernames/passwords)

- **Implementation Notes**:
  - Current: Inconsistent error formats
  - Required: Standardized error schema with enum validation

---

### 2.9 Coordinate Normalization (DPI Scaling)

**User Story 2.9**: As an autonomous agent running on high-DPI displays, I want coordinate normalization, so that my click actions land on the correct elements regardless of system DPI scaling.

**2.9.1** WHEN the system DPI scaling is not 100%, all coordinate-based operations SHALL normalize between logical and physical coordinates
- **Acceptance Criteria**:
  - WHEN DPI scaling is 150%, `click_element` at logical (500, 300) SHALL translate to physical (750, 450) before clicking
  - `check_element_state` SHALL return `bounding_rect` in logical coordinates (pre-scaling)
  - All tool responses SHALL include `dpi_scale_factor` (float, e.g., 1.0, 1.5, 2.0) for transparency
  - The system SHALL detect DPI via `GetDpiForWindow()` Win32 API
  - WHEN DPI is per-monitor (different monitors have different scaling), the system SHALL use DPI of monitor containing target window

- **Implementation Notes**:
  - Current: Unknown if normalization happens
  - Required: DPI detection and coordinate transformation layer

---

## 3. Non-Functional Requirements

### 3.1 Performance

**NFR-1**: WHEN `get_ui_tree` is called with `max_depth=3` on a complex application (≥500 UI elements), it SHALL complete in <2 seconds

**NFR-2**: WHEN `get_ui_tree` applies heuristic pruning, it SHALL limit token payload to ≤5000 tokens for `max_depth=3`

**NFR-3**: WHEN `check_element_state` is called with `selector_type="cached_id"`, it SHALL complete in <100ms

### 3.2 Reliability

**NFR-4**: WHEN any tool encounters ElementNotAvailableException, it SHALL return structured error (not crash) with `error_code="ELEMENT_NOT_AVAILABLE"`

**NFR-5**: WHEN any tool encounters invalid HWND (window closed), it SHALL return error with `error_code="PROCESS_TERMINATED"` and message "Window no longer exists"

**NFR-6**: WHEN DPI scaling is enabled (125%, 150%, 175%, 200%), all coordinate-based operations SHALL normalize correctly (±5px tolerance)

### 3.3 Compatibility

**NFR-7**: The MCP server SHALL work with FlaUI 4.0.0 using UIA2 backend (no visual requirements, works headless)

**NFR-8**: The MCP server SHALL support WinForms, WPF, and Win32 applications that implement UI Automation providers

**NFR-9**: The MCP server SHALL run on Windows 10 Pro/Enterprise/Education (1903+) or Windows 11 for sandbox support

### 3.4 Observability

**NFR-10**: All tool responses SHALL include `execution_time_ms` (integer) for performance debugging

**NFR-11**: WHEN tree pruning removes >90% of elements, the system SHALL log warning to stderr: "Aggressive pruning reduced tree by <N>% to meet token budget"

**NFR-12**: The system SHALL provide `get_capabilities()` tool returning: `sandbox_available`, `os_version`, `flaui_version`, `uia_backend`, `max_depth_supported`, `token_budget`, `features[]`

---

## 4. Edge Cases

### 4.1 Window Focus Stealing Prevention

**Problem**: Agent calls `type_text` on background window, OS blocks focus change due to focus stealing prevention

**Expected Behavior**:
- System attempts `SetFocus()` on target window
- Verify foreground window PID matches target PID
- IF mismatch, retry with minimize-restore cycle: Minimize → Restore → SetFocus
- IF 3 retries fail, return error "Unable to focus window, focus stealing prevention active"

**Current State**: Unknown if retry logic exists

### 4.2 Virtualized List Exploration

**Problem**: Agent calls `get_ui_tree` on File Explorer with 5000 files, only 20 visible

**Expected Behavior**:
- Tree shows 20 visible items
- Tree includes metadata: `{"container_supports_scroll": true, "scroll_percent": 0, "estimated_total_items": 5000}` (if available from container)
- Agent calls `scroll(direction="down", amount="page")` repeatedly to reveal more items

**Current State**: `list_elements` would show 20 items but no scroll tool exists

### 4.3 Dynamic AutomationId Suffix

**Problem**: Element AutomationId changes from `btn_Submit_123` to `btn_Submit_456` on app restart

**Expected Behavior**:
- Agent uses `automation_id_pattern="btn_Submit_\d+"` (regex match)
- OR falls back to `name="Submit"` selector
- System logs warning: "AutomationId changed, using name-based fallback"

**Current State**: Exact match only, agent must handle pattern matching in prompt

### 4.4 Event Queue Overflow

**Problem**: App shows 50 toast notifications while agent processes previous step

**Expected Behavior**:
- Event queue capped at 10 events (FIFO eviction)
- Oldest 40 events discarded
- Next `get_ui_tree()` response includes warning: `{"events_dropped": true, "count": 40}`
- Agent may adjust strategy (increase event queue size via config, or poll more frequently)

**Current State**: No event queue implementation

### 4.5 Dirty Tree Performance (Legacy Apps)

**Problem**: SAP GUI tree returns 100,000 UIA elements (estimated 50MB XML), exceeds MCP message limits

**Expected Behavior**:
- System applies aggressive pruning: skip all Pane/Group/Image, limit `max_depth=2` by default
- IF tree still exceeds token budget, return error with partial tree + warning
- Error includes: `{"error_code": "TREE_TOO_BIG", "tree_stats": {"element_count": 100000, "pruned_count": 95000, "token_count": 8234}}`
- Agent may retry with `max_depth=1` or target specific sub-window

**Current State**: Unknown if pruning is aggressive enough

### 4.6 Cross-Process File Access Attack (SECURITY)

**Problem**: Agent explores text editor → clicks "Open File" → File Explorer opens (new PID) → agent navigates to `C:\Users\jhedin\Documents` → right-clicks `important-file.docx` → Delete

**Defense Layers**:
1. **Layer 1 (PRIMARY)**: Sandbox isolation - host `C:\Users\jhedin\Documents` does NOT exist inside sandbox VM. Agent sees empty directory, cannot delete real files.
2. **Layer 2 (SECONDARY)**: After `attach_to_process(notepad.exe)`, `get_ui_tree()` only returns Notepad windows. When File Explorer opens, agent must explicitly call `attach_to_process(explorer.exe)` to interact. Reduces accidental clicks but does NOT prevent intentional navigation.
3. **Layer 3 (TERTIARY)**: Destructive actions trigger confirmation (optional feature)

**Failure Mode if no sandbox**: Agent deletes real production file → **UNACCEPTABLE**.

**Current State**: No sandboxing, no defense layers → **critical security gap**

### 4.7 Accidental Sensitive Folder Mapping

**Problem**: Developer creates .wsb file, accidentally maps `C:\Users\jhedin\Documents` as writable

**Expected Behavior**:
- MCP server validates .wsb config before launching sandbox
- Detects forbidden path via canonical path resolution (resolves symlinks, normalizes case)
- Returns error: `{"error_code": "UNSAFE_CONFIG", "violations": ["MappedFolder[0]: HostFolder points to sensitive path C:\Users\jhedin\Documents"]}`
- Developer corrects config to use dedicated test folders `C:\AgentTestInputs`, `C:\AgentTestOutputs`

**Current State**: No .wsb validation → developer mistake = data loss

### 4.8 Network Isolation Bypass

**Problem**: Agent explores email client → clicks "Send" → real email sent to customer

**Expected Behavior**:
- Sandbox has `<Networking>Disable</Networking>` in .wsb config
- Email send operation fails (no network access)
- MCP server validates .wsb before launch, rejects if `<Networking>Enable</Networking>`

**Current State**: No network isolation enforcement

---

## 5. Out of Scope

The following are explicitly OUT OF SCOPE for this specification:

1. **Visual AI (OmniParser/OCR)**: Coordinate-based clicking via bounding boxes overlaid on screenshots - agents should use structural selectors (AutomationId, Name) not pixel analysis
2. **COM Automation**: Direct API calls to Office applications (Excel.Application.Workbooks.Open) - bypass GUI automation entirely
3. **Test Case Execution**: Running generated Playwright/PyWinAuto scripts - generation is in scope, execution is not
4. **Multi-Application Orchestration**: Coordinating actions across multiple running apps simultaneously - focus on single-app exploration with PID isolation
5. **Input Recording**: Capturing human interactions as replay logs - focus on agent-driven exploration
6. **Automatic .wsb Generation**: MCP won't auto-generate sandbox configs - user provides pre-configured .wsb files
7. **Sandbox Performance Optimization**: Warm sandbox pooling, snapshot/restore - ephemeral isolation is priority over speed

---

## 6. Tool Comparison Matrix

| Tool | Current Status | Required | Priority | Gap Description |
|------|---------------|----------|----------|-----------------|
| `get_ui_tree` | ❌ Missing | ✅ Yes | **P0** | Have `list_elements` but not hierarchical with pruning |
| `expand_collapse` | ❌ Missing | ✅ Yes | **P0** | Cannot explore nested menus/trees |
| `scroll` | ❌ Missing | ✅ Yes | **P0** | Cannot explore virtualized lists |
| `check_element_state` | ⚠️ Partial | ✅ Yes | **P0** | Have `element_exists` + `get_property`, need unified call |
| `get_process_info` | ❌ Missing | ✅ Yes | P1 | Cannot track process continuity for focus management |
| `subscribe_to_events` | ⚠️ Placeholder | ✅ Yes | P1 | `listen_for_event` exists but not implemented |
| `launch_app_sandboxed` | ❌ Missing | ✅ Yes | **P0** | Safety-critical - prevent data loss during testing |
| `close_sandbox` | ❌ Missing | ✅ Yes | **P0** | Clean up ephemeral test environments |
| `get_capabilities` | ❌ Missing | ✅ Yes | P1 | Detect if sandboxing is available |
| **PROCESS SCOPING** | | | | |
| PID filtering in `get_ui_tree` | ❌ Missing | ✅ Yes | **P0** | Prevent cross-app interaction accidents |
| Session-level `active_pid` | ❌ Missing | ✅ Yes | **P0** | Default scope after attach_to_process |
| Foreign process detection | ❌ Missing | ✅ Yes | P1 | Handle system dialogs (File Open, Print) |
| **OPTIONAL FEATURES** | | | | |
| `find_element_near_anchor` | ❌ Missing | ⚠️ Nice-to-have | P2 | Self-healing can be done in prompt logic |
| `automation_id_pattern` (regex) | ❌ Missing | ⚠️ Nice-to-have | P2 | Regex selectors for dynamic IDs |
| `confirm_action` | ❌ Missing | ⚠️ Nice-to-have | P2 | Human-in-loop for destructive actions |

**Priority Legend**:
- **P0**: Blocking - agents cannot function without this
- **P1**: Important - agents can work around but significantly degraded
- **P2**: Nice-to-have - improves agent reliability but not critical

---

## 7. Success Criteria

This feature is successful when:

**Core Functionality:**
1. ✅ An autonomous agent can explore Calculator and generate ≥3 test cases covering distinct user flows
2. ✅ An agent can detect a bug (button click causing no state change) and report with reproduction steps
3. ✅ An agent can identify a UX gap (missing "Clear" button) by comparing against CRUD heuristics
4. ✅ `get_ui_tree` token payload is <5000 tokens for `max_depth=3` on complex apps (≥500 elements)
5. ✅ Agent can explore nested menu (File > Settings > Advanced) using `expand_collapse`
6. ✅ Agent can scroll through large list (1000+ items) using `scroll` and find target item
7. ✅ Agent receives notifications when unexpected dialog appears via event queue

**Safety & Isolation:**
8. ✅ Agent can launch Notepad in Windows Sandbox, type text, save to mapped output folder, close sandbox - host files untouched
9. ✅ After `attach_to_process(notepad.exe)`, `get_ui_tree()` returns ONLY Notepad windows (not Chrome, Slack, etc.)
10. ✅ Agent exploring text editor accidentally clicks "Delete All" → optional confirmation prevents execution (if enabled)
11. ✅ On system without Windows Sandbox, `get_capabilities()` returns `sandbox_available=false` and agent prompts user

**Security Criteria (MUST NOT HAPPEN)**:
- ❌ **Agent MUST NOT delete host files** even if File Explorer is opened inside sandbox
- ❌ **Agent MUST NOT interact with unrelated processes** (Slack, Email) while exploring Calculator
- ❌ **Agent MUST NOT send real emails/API calls** from sandboxed apps (network disabled)
- ❌ **Developer MUST NOT accidentally map sensitive folders** (validation catches mistakes)

**Performance Criteria:**
- `get_ui_tree` completes in <2s for complex apps (NFR-1)
- `check_element_state` with caching completes in <100ms (NFR-3)
- PID-scoped `get_ui_tree` completes in <500ms (vs 2s desktop-wide baseline)

**Failure Criteria (UNACCEPTABLE)**:
- ❌ Agent gets stuck in loop because it cannot detect state changes (no `state_changed` feedback)
- ❌ Agent hallucinates elements because `get_ui_tree` returned truncated tree without warning
- ❌ Agent cannot explore menus because `expand_collapse` is missing
- ❌ MCP server crashes or hangs when `get_ui_tree` is called on legacy app with 50k+ elements

---

## 8. References

- **Windows Sandbox Architecture Research**: See `research-sandbox-architecture.md` for detailed analysis of isolation strategies, .wsb configuration patterns, and security model justification
- **Background Automation Research**: See `research-background-automation.md` for VDD integration, registry configuration, and InjectTouchInput implementation
- **EARS Format**: Easy Approach to Requirements Syntax (trigger + action + result)
- **UIA Documentation**: Microsoft UI Automation API reference
- **FlaUI Library**: https://github.com/FlaUI/FlaUI (v4.0.0 with UIA2 backend)
- **MCP Protocol**: Model Context Protocol specification (JSON-RPC 2.0 over stdio)
- **InjectTouchInput API**: Win32 pointer injection for touch/pen input
- **IddCx (Indirect Display Driver)**: Windows virtual display driver framework
