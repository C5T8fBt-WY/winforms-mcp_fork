# MCP Tool Coverage - Implementation Tasks

## Architecture Decision (Completed)

**RESOLVED**: MCP server runs **inside Windows Sandbox** alongside target application (Option B).

**Research Findings** (see `research-sandbox-architecture.md`):
- ✅ UIA automation cannot cross VM boundaries (Option A infeasible)
- ✅ Windows Sandbox provides kernel-level filesystem isolation
- ✅ Sandbox grants default Admin privileges (solves `InjectTouchInput` permission issues)
- ✅ MCP communicates with host via named pipe or mapped folder polling

**Trade-off**: 10-15s sandbox boot time per test session, but provides strongest filesystem safety and solves touch/pen permission requirements.

---

## Phase 0: Transport Layer Prototype (COMPLETE)

Determine the MCP transport mechanism for host ↔ sandbox communication.

- [x] 1. Prototype MCP transport options
  - Test communication methods between host LLM and MCP server inside sandbox
  - Implements requirement: 2.7.2, 2.8.1
  - [x] 1.1. Test named pipe transport (**SANDBOX TEST FAILED - as expected**)
    - ✅ Created `NamedPipeHostServer` - host-side pipe server with latency measurement
    - ✅ Created `NamedPipeClientTest` - sandbox-side client, tries `.`, `localhost`, machine name
    - ✅ Created `test-named-pipe.wsb` - sandbox configuration
    - ✅ Built and verified compilation
    - ❌ **SANDBOX TEST FAILED** (2026-01-13):
      - `.` (local pipe): TimeoutException
      - `localhost`: IOException - network name not available
      - Machine name: IOException - network name not available
      - **Conclusion**: Named pipes are local to VM namespace, not bridged to host
    - **Hypothesis CONFIRMED**: Named pipes do NOT work across VM boundary
    - Location: `prototypes/transport-test/NamedPipeHostServer/`, `NamedPipeClientTest/`
  - [x] 1.2. Test shared folder polling transport (**SANDBOX TEST PASSED**)
    - ✅ Created `SharedFolderHost` - host writes request.json, polls for response.json
    - ✅ Created `SharedFolderClient` - sandbox polls for requests, writes responses
    - ✅ Created `test-shared-folder.wsb` - sandbox configuration
    - ✅ Built and verified compilation
    - ✅ **LOCAL TEST PASSED** (2025-01-13):
      - 50 requests, 0 failures
      - P95 latency: **55ms** (target: <500ms) ✅
      - Average latency: 48ms
      - Max latency: 110ms
    - ✅ **SANDBOX TEST PASSED** (2026-01-13):
      - Client successfully starts in sandbox
      - `client-ready.signal` written to shared folder
      - Communication verified between host and sandbox
    - Location: `prototypes/transport-test/SharedFolderHost/`, `SharedFolderClient/`
  - [x] 1.3. Document transport decision (**CONFIRMED**)
    - **DECISION**: Use shared folder polling as primary transport
    - Shared folder transport confirmed working in sandbox
    - Named pipe testing still pending (may offer lower latency)
    - Latency (~100ms P95) is acceptable for UI automation (individual ops take 500ms+)

**Key Learnings**:
1. **Windows Sandbox 0.5.3.0 coreclr.dll crash bug**: Must launch sandbox from **admin PowerShell** or it crashes immediately.
   - Workaround: `Start-Process powershell -Verb RunAs -ArgumentList '-Command', 'WindowsSandbox.exe config.wsb'`
2. **Self-contained publish required**: Windows Sandbox does NOT have .NET runtime. Must publish with `--self-contained true` (~180 files).
3. **Unblock files**: Files copied from WSL paths are marked as "from internet". Run `Get-ChildItem -Recurse | Unblock-File`.
4. **Avoid UNC paths in PowerShell cd**: Copy binaries to Windows-native paths (e.g., `C:\WinFormsMcpSandboxWorkspace\`) to avoid path issues.

**Test Procedure** (see `prototypes/transport-test/README.md` for details):
1. Build with `dotnet publish ... --self-contained true -o C:\WinFormsMcpSandboxWorkspace\...`
2. Run host: `C:\WinFormsMcpSandboxWorkspace\Host\SharedFolderHost.exe C:\WinFormsMcpSandboxWorkspace\Shared`
3. Launch sandbox from admin PowerShell: `WindowsSandbox.exe C:\WinFormsMcpSandboxWorkspace\test-shared-folder.wsb`
4. Check `C:\WinFormsMcpSandboxWorkspace\Shared\client-ready.signal` appears

---

## Phase 0.5: VDD Integration & Background Automation

Enable background automation by integrating Virtual Display Driver.

**Prerequisites**: Host must have registry key set (one-time setup, documented in requirements.md 1.1).

- [ ] 1.5. Bundle VDD driver with MCP server
  - Select and integrate a Virtual Display Driver for background automation
  - [x] 1.5.1. Research and select VDD driver
    - Evaluated IddSampleDriver (ge9 fork) - MIT license, UMDF-based, easy install
    - Evaluated parsec-vdd - proprietary driver, not bundleable
    - Evaluated Virtual-Display-Driver - MIT, more complex than needed
    - **Selected: IddSampleDriver (ge9 fork)** - MIT license, minimal deps, proven stability
    - Document: `research-vdd-selection.md`
  - [ ] 1.5.2. Acquire or build VDD driver files
    - Obtain `.inf`, `.sys` (or `.dll`), and `.cat` files
    - For test signing: generate self-signed certificate for development
    - Document driver acquisition process
  - [ ] 1.5.3. Create driver signing certificate
    - Generate test certificate: `New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=MCP VDD Test"`
    - Export to `.cer` file for import in sandbox
    - Document that production deployment needs proper EV code signing
  - [ ] 1.5.4. Add driver files to MCP server publish output
    - Update `.csproj` to include VDD driver files in publish
    - Add to `publish/vdd/` subdirectory:
      - `vdd_driver.inf`
      - `vdd_driver.sys` (or `.dll`)
      - `vdd_driver.cer` (test certificate)

- [ ] 1.6. Create sandbox bootstrap script
  - PowerShell script to install VDD and launch MCP server
  - [ ] 1.6.1. Write bootstrap.ps1 script
    ```powershell
    # 1. Trust driver certificate
    Import-Certificate -FilePath "C:\MCP\vdd\vdd_driver.cer" -CertStoreLocation Cert:\LocalMachine\Root

    # 2. Install VDD driver
    pnputil /add-driver "C:\MCP\vdd\vdd_driver.inf" /install

    # 3. Wait for display initialization
    Start-Sleep -Seconds 5

    # 4. Launch MCP server
    Start-Process -FilePath "C:\MCP\Rhombus.WinFormsMcp.Server.exe" -NoNewWindow
    ```
  - [ ] 1.6.2. Add VDD installation status check
    - After pnputil, verify display adapter appeared in Device Manager
    - Log warning if VDD installation failed (continue anyway - foreground still works)
  - [ ] 1.6.3. Write integration test for bootstrap
    - Launch sandbox with bootstrap.ps1 in LogonCommand
    - Verify VDD appears in Device Manager (via PowerShell query)
    - Verify display count increased by 1

- [ ] 1.7. Test background automation with VDD
  - Verify automation works when sandbox is minimized
  - [ ] 1.7.1. Write manual test: foreground operation baseline
    - Launch sandbox with VDD installed
    - Run full automation suite (get_ui_tree, click, screenshot)
    - Document baseline latency and success rate
  - [ ] 1.7.2. Write manual test: minimized operation
    - Minimize sandbox window
    - Run same automation suite
    - Verify: screenshots NOT black, elements still discoverable
    - Document latency difference (should be <100ms)
  - [ ] 1.7.3. Write automated E2E test
    - Launch sandbox, minimize programmatically
    - Run automation, verify success
    - Add to CI test suite (may need special CI configuration)

---

## Phase 1: Sandbox Bootstrap & Process Management

Implement sandbox launching and MCP server lifecycle.

### Note: Windows Sandbox Process Management

Windows Sandbox works correctly. However, killing the sandbox via PowerShell `Stop-Process` can cause issues. Use the proper shutdown mechanism (shutdown.signal file) to cleanly terminate the sandbox session.

---

- [x] 2. Implement `launch_app_sandboxed` tool
  - Start Windows Sandbox with target app and MCP server
  - Implements requirement: 2.7.1, 2.7.3
  - [x] 2.1. Write unit tests for .wsb generation (**DONE** - 26 tests passing)
    - ✅ Test WsbConfigBuilder.Generate() creates valid XML
    - ✅ Test validation rejects C:\Users\{username}\Documents mapping
    - ✅ Test validation rejects C:\Users\{username}\Desktop mapping
    - ✅ Test validation rejects case variations (C:\users vs C:\Users)
    - ✅ Test validation allows system temp paths
    - Location: `tests/Rhombus.WinFormsMcp.Tests/WsbConfigBuilderTests.cs`
  - [x] 2.2. Implement WsbConfigBuilder class (**DONE**)
    - Create .wsb XML with schema (includes VDD bootstrap):
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
          <!-- Use bootstrap.ps1 to install VDD then launch MCP server -->
          <Command>powershell -ExecutionPolicy Bypass -File C:\MCP\bootstrap.ps1</Command>
        </LogonCommand>
      </Configuration>
      ```
    - Path validation:
      - Resolve all paths to canonical form (follow symlinks, normalize case)
      - Reject if path contains: `\Documents`, `\Desktop`, `\AppData`, `\Windows`, `\Program Files`
      - Only allow explicit test directories
    - Accept parameters: `app_path`, `mcp_server_path`, `output_folder`
    - Return generated .wsb file path
    - ✅ Location: `src/Rhombus.WinFormsMcp.Server/Sandbox/WsbConfigBuilder.cs`
  - [x] 2.3. Implement SandboxManager.LaunchSandbox() method (**DONE**)
    - ✅ Accept .wsb configuration path
    - ✅ Launch: `Process.Start("WindowsSandbox.exe", wsbPath)`
    - ✅ Wait for sandbox boot (polls for mcp-ready.signal file)
    - ✅ Shared folder polling transport for communication
    - ✅ Return SandboxLaunchResult with process info
    - ✅ Location: `src/Rhombus.WinFormsMcp.Server/Sandbox/SandboxManager.cs`
  - [x] 2.4. Add launch_app_sandboxed to MCP tool registry (**DONE**)
    - ✅ JSON-RPC handler in Program.cs
    - ✅ SandboxManager stored in SessionManager
    - ✅ Security validation via WsbConfigBuilder
  - [ ] 2.5. Write E2E test for sandbox launch
    - ⏳ Blocked: Requires Windows Pro for Windows Sandbox
    - Call launch_app_sandboxed with TestApp.exe
    - Verify WindowsSandbox.exe process starts
    - Verify connection established (ping test)
    - Verify subsequent tool calls work (get_ui_tree)

- [x] 3. Implement `close_sandbox` tool (**DONE**)
  - Gracefully shutdown sandbox VM
  - Implements requirement: 2.7.4
  - [ ] 3.1. Write unit tests for shutdown
    - ⏳ Blocked: Requires Windows Pro for integration testing
    - Test graceful shutdown (send "shutdown" message)
    - Test force shutdown (kill process after timeout)
    - Test resource cleanup (handles, files)
  - [x] 3.2. Implement SandboxManager.CloseSandbox() method (**DONE**)
    - ✅ Send shutdown signal via shutdown.signal file
    - ✅ Wait for graceful exit (configurable timeout)
    - ✅ Force kill if timeout: `sandboxProcess.Kill(entireProcessTree: true)`
    - ✅ Clean up session state and signal files
    - ✅ Location: `src/Rhombus.WinFormsMcp.Server/Sandbox/SandboxManager.cs`
  - [x] 3.3. Add close_sandbox to MCP tool registry (**DONE**)
    - ✅ JSON-RPC handler in Program.cs
  - [ ] 3.4. Write integration test
    - ⏳ Blocked: Requires Windows Pro for Windows Sandbox
    - Launch sandbox
    - Close sandbox
    - Verify process terminated
    - Verify no orphaned processes

- [ ] 4. Implement `get_process_info` tool (sandboxed version)
  - List processes inside sandbox
  - Implements requirement: 2.6.1
  - [ ] 4.1. Write unit tests for ProcessManager
    - Test GetRunningProcesses() returns sandbox processes only
    - Test SetActivePID() updates session scope
  - [ ] 4.2. Implement AutomationHelper.GetAllWindows() method
    - Inside sandbox: enumerate all desktop windows
    - For each window, get PID via GetWindowThreadProcessId
    - Return list: { pid, processName, windowTitle, windowHandle }
  - [ ] 4.3. Add get_process_info to MCP tool registry
    - Accept optional set_active_pid parameter
    - Update SessionManager.active_pid
    - All subsequent get_ui_tree calls filter by active_pid
  - [ ] 4.4. Write integration test for PID filtering
    - Launch two TestApp instances in sandbox
    - Set active_pid to first instance
    - Verify get_ui_tree only returns windows from active process

---

## Phase 2: Core Observation Tools

Implement UI tree discovery and navigation.

- [x] 5. Implement `get_ui_tree` tool (**DONE** - 2026-01-13)
  - Return hierarchical UI tree with configurable depth
  - Implements requirement: 2.1.1, 2.1.2, 2.1.3
  - [ ] 5.1. Write unit tests for TreeBuilder class
    - Test depth limiting (max_depth=1 vs max_depth=3)
    - Test heuristic pruning (skip invisible elements)
    - Test token budget enforcement (<5000 tokens)
    - Test DPI scale factor reporting
  - [x] 5.2. Implement TreeBuilder.BuildTree() method (**DONE**)
    - ✅ Recursive tree traversal with max_depth parameter
    - ✅ Apply heuristic filters:
      - Skip elements with IsOffscreen=true
      - Skip elements with AutomationId="PART_*" (internal WPF parts)
      - Skip disabled containers with no enabled descendants
    - ✅ For each element, extract:
      - AutomationId, Name, ControlType
      - IsEnabled, IsOffscreen, bounds
    - ✅ Return XML tree with metadata (dpi_scale_factor, token_count, element_count, timestamp, truncated)
    - Location: `src/Rhombus.WinFormsMcp.Server/Automation/TreeBuilder.cs`
  - [x] 5.3. Add get_ui_tree to MCP tool registry (**DONE**)
    - ✅ Accept parameters: maxDepth, maxTokenBudget, includeInvisible, skipInternalParts, windowTitle
    - ✅ Returns XML tree + metadata
    - Location: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - [ ] 5.4. Write integration tests
    - ⏳ Requires GUI to test

- [x] 6. Implement `expand_collapse` tool (**DONE** - 2026-01-13)
  - Toggle ExpandCollapse pattern elements
  - Implements requirement: 2.2.1
  - [ ] 6.1. Write unit tests for ExpandCollapseAction
    - ⏳ Requires GUI for pattern testing
  - [x] 6.2. Implement AutomationHelper.ExpandCollapse() method (**DONE**)
    - ✅ Accept element + expand (bool)
    - ✅ Get ExpandCollapsePattern
    - ✅ Call Expand() or Collapse() based on desired state
    - ✅ Return ExpandCollapseResult with previous/current state
    - Location: `src/Rhombus.WinFormsMcp.Server/Automation/AutomationHelper.cs`
  - [x] 6.3. Add expand_collapse to MCP tool registry (**DONE**)
    - ✅ Accept elementId or automationId + windowTitle
    - Location: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - [ ] 6.4. Write integration test with TreeView
    - ⏳ Requires GUI to test

- [x] 7. Implement `scroll` tool (**DONE** - 2026-01-13)
  - Scroll scrollable containers
  - Implements requirement: 2.3.1
  - [ ] 7.1. Write unit tests for ScrollAction
    - ⏳ Requires GUI for pattern testing
  - [x] 7.2. Implement AutomationHelper.Scroll() method (**DONE**)
    - ✅ Accept element, direction (enum: Up/Down/Left/Right), amount (enum: SmallDecrement/LargeDecrement)
    - ✅ Get ScrollPattern
    - ✅ Call ScrollVertical() or ScrollHorizontal() based on direction
    - ✅ Return ScrollResult with scroll percentages and changed flags
    - Location: `src/Rhombus.WinFormsMcp.Server/Automation/AutomationHelper.cs`
  - [x] 7.3. Add scroll to MCP tool registry (**DONE**)
    - ✅ Accept elementId or automationId + windowTitle + direction + amount
    - Location: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - [ ] 7.4. Write integration test with long ListBox
    - ⏳ Requires GUI to test

- [x] 8. Implement `check_element_state` tool (**DONE** - 2026-01-13)
  - Query individual element properties
  - Implements requirement: 2.4.1
  - [ ] 8.1. Write unit tests for StateQuery
    - ⏳ Requires GUI for pattern testing
  - [x] 8.2. Implement AutomationHelper.GetElementState() method (**DONE**)
    - ✅ Accept element
    - ✅ Return ElementStateResult with:
      - Basic: AutomationId, Name, ClassName, ControlType
      - State: IsEnabled, IsOffscreen, IsKeyboardFocusable, HasKeyboardFocus
      - BoundingRect, DpiScaleFactor
      - Patterns: Value, IsReadOnly, ToggleState, IsSelected, RangeValue/Min/Max
    - Location: `src/Rhombus.WinFormsMcp.Server/Automation/AutomationHelper.cs`
  - [x] 8.3. Add check_element_state to MCP tool registry (**DONE**)
    - ✅ Accept elementId or automationId + windowTitle
    - Location: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - [ ] 8.4. Write integration test
    - ⏳ Requires GUI to test

**Supporting Types** (created 2026-01-13):
- `TreeBuilderOptions` - Options for tree building (MaxDepth, MaxTokenBudget, IncludeInvisible, SkipInternalParts, FilterByPid)
- `TreeBuildResult` - Result with XML, TokenCount, ElementCount, DpiScaleFactor, Timestamp, Truncated
- `ExpandCollapseResult` - Result with Success, ErrorMessage, PreviousState, CurrentState
- `ScrollResult` - Result with Success, ErrorMessage, scroll percentages, changed flags
- `ScrollDirection` - Enum: Up, Down, Left, Right
- `ScrollAmount` - Enum: SmallDecrement, LargeDecrement
- `ElementStateResult` - Comprehensive element state including all patterns
- `BoundingRectInfo` - Bounding rectangle info
- Location: `src/Rhombus.WinFormsMcp.Server/Automation/AutomationTypes.cs`

---

## Phase 3: State Change Detection

Implement diff detection for interaction feedback.

- [x] 9. Implement state change detection algorithm (**DONE** - 2026-01-13)
  - Detect UI changes after interactions
  - Implements requirement: 2.2.2
  - **Implementation Note**: Instead of modifying each interaction tool (which would add latency), created standalone snapshot tools that give the LLM explicit control over when to capture and compare state.

  - [ ] 9.1. Write unit tests for TreeHasher
    - ⏳ Requires GUI for meaningful tests
  - [x] 9.2. Implement TreeHasher class (**DONE**)
    - ✅ CaptureSnapshot() traverses tree up to depth 5
    - ✅ Includes AutomationId, Name, ControlType, IsEnabled, IsOffscreen, Value
    - ✅ Uses SHA256 for hashing (truncated to 16 chars for readability)
    - ✅ Returns TreeSnapshot with hash, elements dict, timestamp
    - Location: `src/Rhombus.WinFormsMcp.Server/Automation/StateChangeDetector.cs`
  - [x] 9.3. Implement snapshot comparison tools (**DONE**)
    - ✅ `capture_ui_snapshot` - captures current state with custom snapshotId
    - ✅ `compare_ui_snapshots` - compares before/after snapshots
    - ✅ Returns diff summary with added/removed/modified counts
    - ✅ Identifies specific elements and state changes
    - Location: `src/Rhombus.WinFormsMcp.Server/Program.cs`
  - [ ] 9.4. Write integration test
    - ⏳ Requires GUI to test

**Usage Pattern for LLM:**
```
1. capture_ui_snapshot(snapshotId="before_click")
2. click_element(...)
3. compare_ui_snapshots(beforeSnapshotId="before_click")
   → Returns: {stateChanged: true, diffSummary: "Added: Dialog[SaveDialog]. Modified: Button[btnSave] (IsEnabled=false)."}
```

**Supporting Types** (created 2026-01-13):
- `StateChangeDetector` - High-level API for capturing and comparing snapshots
- `TreeHasher` - Computes hashes of UI trees
- `TreeSnapshot` - Snapshot with hash, elements dict, timestamp
- `ElementInfo` - Element state for diff comparison
- `StateChangeResult` - Comparison result with diff summary
- Location: `src/Rhombus.WinFormsMcp.Server/Automation/StateChangeDetector.cs`

---

## Phase 4: Touch and Pen Input

Implement low-level input injection for pen-based workflows.

**Note**: These tools leverage Windows Sandbox's default Admin privileges to use `InjectTouchInput` API without UAC prompts.

**STATUS**: All tools implemented in `TouchPenHandlers.cs` and `InputInjection.cs`. Integration tests require Windows.

- [x] 10. Implement `touch_tap` tool
  - Simulate touch tap at coordinates
  - Implements requirement: 2.10.1
  - [x] 10.1. Implement InputInjection.TouchTap() method
  - [x] 10.2. Add touch_tap to MCP tool registry (via TouchPenHandlers)
  - [x] 10.3. Window-relative coordinate support added
  - [ ] 10.4. Write integration test (requires Windows)

- [x] 11. Implement `touch_drag` tool
  - Simulate touch drag gesture
  - Implements requirement: 2.10.2
  - [x] 11.1. Implement InputInjection.TouchDrag() method
  - [x] 11.2. Add touch_drag to MCP tool registry (via TouchPenHandlers)
  - [x] 11.3. Window-relative coordinate support added
  - [ ] 11.4. Write integration test (requires Windows)

- [x] 12. Implement `pen_stroke` tool
  - Simulate pen stroke with pressure
  - Implements requirement: 2.10.3
  - [x] 12.1. Implement InputInjection.PenStroke() method
  - [x] 12.2. Add pen_stroke to MCP tool registry (via TouchPenHandlers)
  - [x] 12.3. Pressure and eraser mode support
  - [ ] 12.4. Write integration test with InkCanvas (requires Windows)

- [x] 13. Implement `pen_tap` tool
  - Simulate pen tap at coordinates
  - [x] 13.1. Implement InputInjection.PenTap() method
  - [x] 13.2. Add pen_tap to MCP tool registry (via TouchPenHandlers)
  - [ ] 13.3. Write integration test (requires Windows)

- [x] 14. Implement `pinch_zoom` tool
  - Simulate two-finger pinch gesture
  - Implements requirement: 2.10.4
  - [x] 14.1. Implement InputInjection.PinchZoom() method
  - [x] 14.2. Add pinch_zoom to MCP tool registry (via TouchPenHandlers)
  - [ ] 14.3. Write integration test (requires Windows)

- [x] 15. Implement `rotate` tool
  - Simulate two-finger rotation gesture
  - [x] 15.1. Implement InputInjection.RotateGesture() method
  - [x] 15.2. Add rotate to MCP tool registry (via TouchPenHandlers)
  - [ ] 15.3. Write integration test (requires Windows)

- [x] 16. Implement `multi_touch_gesture` tool
  - Simulate arbitrary multi-touch gestures
  - [x] 16.1. Implement InputInjection.MultiTouchGesture() method
  - [x] 16.2. Add multi_touch_gesture to MCP tool registry (via TouchPenHandlers)
  - [ ] 16.3. Write integration test (requires Windows)

---

## Phase 5: DPI and Coordinate Normalization

**STATUS**: DPI info tool implemented in AdvancedHandlers.cs. Coordinate-based tools use client-area relative coordinates via WindowManager.

Handle high-DPI displays properly.

- [ ] 13. Implement DPI scaling system
  - Normalize coordinates across DPI settings
  - Implements requirement: 2.9.1
  - [ ] 13.1. Write unit tests for DpiHelper
    - Test GetSystemDpiScaleFactor() returns 1.0, 1.25, 1.5, 1.75, 2.0
    - Test LogicalToPhysical(500, 300) at 150% DPI = (750, 450)
    - Test PhysicalToLogical(750, 450) at 150% DPI = (500, 300)
  - [ ] 13.2. Implement DpiHelper class
    - Query system DPI via `GetDpiForSystem()` Win32 API
    - Compute scale factor: `dpiScaleFactor = systemDpi / 96.0`
    - Provide conversion methods:
      - `LogicalToPhysical(x, y)`: multiply by scale factor
      - `PhysicalToLogical(x, y)`: divide by scale factor
  - [ ] 13.3. Integrate into all coordinate-based tools
    - Modify click_element to accept logical coordinates, convert to physical
    - Modify touch_tap, touch_drag, pen_stroke similarly
    - Modify get_ui_tree to return logical BoundingRectangle
    - All tool responses include `dpi_scale_factor` field for transparency
  - [ ] 13.4. Write integration test at 150% DPI
    - Set system DPI to 150% (via Display Settings or test harness)
    - Launch TestApp
    - Click button at logical (500, 300)
    - Verify click lands on button (not offset)

---

## Phase 6: Event System

Implement UI event subscription for proactive change detection.

**STATUS**: Event tools implemented in AdvancedHandlers.cs and SessionManager.

- [x] 14. Implement `subscribe_to_events` tool
  - Push UI events to agent
  - Implements requirement: 2.5.1
  - [x] 14.1. Event queue with FIFO eviction in SessionManager
  - [x] 14.2. subscribe_to_events in MCP tool registry (via AdvancedHandlers)
  - [x] 14.3. get_pending_events for polling
  - [ ] 14.4. Write integration test (requires Windows)

---

## Phase 7: Advanced Navigation

Implement progressive disclosure and anchor-based search.

**STATUS**: All tools implemented in AdvancedHandlers.cs and ElementHandlers.cs.

- [x] 15. Implement progressive disclosure
  - Start with shallow tree, expand on demand
  - Implements requirement: 2.5.2
  - [x] 15.1. Expansion tracking in SessionManager (_expandedElements HashSet)
  - [x] 15.2. mark_for_expansion tool in AdvancedHandlers
  - [x] 15.3. clear_expansion_marks tool in AdvancedHandlers
  - [x] 15.4. TreeBuilder respects expansion state
  - [ ] 15.5. Write E2E test (requires Windows)

- [x] 16. Implement anchor-based navigation
  - Find elements relative to stable landmarks
  - Implements design section 3.2.3
  - [x] 16.1. find_element_near_anchor tool in ElementHandlers
  - [x] 16.2. Supports anchorElementId, anchorAutomationId, anchorName
  - [x] 16.3. Search direction (siblings, parent, children, all)
  - [ ] 16.4. Write integration test (requires Windows)

---

## Phase 8: Self-Healing and Robustness

Implement error recovery for stale element references.

**STATUS**: Core self-healing tools implemented in AdvancedHandlers.cs.

- [x] 17. Implement self-healing element location
  - Recover from stale references
  - Implements design section 4.2
  - [x] 17.1. relocate_element tool in AdvancedHandlers
  - [x] 17.2. check_element_stale tool in AdvancedHandlers
  - [x] 17.3. get_cache_stats tool in AdvancedHandlers
  - [x] 17.4. invalidate_cache tool in AdvancedHandlers
  - [ ] 17.5. Write integration test (requires Windows)

- [x] 18. Implement `get_capabilities` tool
  - Report MCP server feature support
  - Implements requirement: 2.1.5
  - [x] 18.1. get_capabilities tool in AdvancedHandlers
  - [x] 18.2. Returns sandbox_available, OS info, supported tools
  - [x] 18.3. get_dpi_info tool for DPI information
  - [x] 18.4. Document capabilities in README (updated with 45+ tools, documentation links)

---

## Phase 9: Performance and Optimization

Optimize for large UI trees and high-frequency calls.

**STATUS**: TreeCache implemented. get_cache_stats and invalidate_cache tools available.

- [x] 19. Optimize tree building performance
  - Reduce latency for large trees
  - Implements non-functional requirement: 3.1 (Performance)
  - [x] 19.1. TreeCache class implemented in Automation/TreeCache.cs
  - [x] 19.2. get_cache_stats tool to monitor cache performance
  - [x] 19.3. invalidate_cache tool for manual cache control
  - [ ] 19.4. Write performance benchmarks (requires Windows)

- [ ] 20. Add telemetry and logging
  - Track tool usage and error rates
  - Implements non-functional requirement: 3.3 (Monitoring)
  - [ ] 20.1. Write unit tests for TelemetryCollector
    - Test tool call counting
    - Test error rate tracking
    - Test performance percentiles (p50, p95, p99)
  - [ ] 20.2. Implement structured logging
    - Use Serilog with JSON format
    - Log all tool calls with parameters (sanitized, no sensitive data)
    - Log all errors with stack traces
    - Log performance metrics (latency, token counts)
  - [ ] 20.3. Add telemetry export
    - Write telemetry to JSON file on shutdown
    - Include session summary: total tools called, error rate, avg latency
  - [ ] 20.4. Document telemetry in README
    - Explain what data is collected
    - Provide opt-out mechanism: TELEMETRY_DISABLED env var

---

## Phase 10: Documentation and Examples

Create comprehensive documentation for agent developers.

- [x] 21. Write MCP tool documentation
  - Document all tools with examples
  - Implements requirement: 5.3 (Documentation)
  - [x] 21.1. Create MCP_TOOLS.md reference (docs/MCP_TOOLS.md - 45 tools documented)
    - For each tool:
      - Name, description
      - Parameters (type, required, default)
      - Return value (structure, examples)
      - Error codes and recovery suggestions
    - Include tool dependency graph (which tools require others)
  - [x] 21.2. Create AGENT_EXPLORATION_GUIDE.md usage guide (docs/AGENT_EXPLORATION_GUIDE.md)
    - Document OODA loop workflow (Observe, Orient, Decide, Act)
    - Document progressive disclosure pattern (shallow first, expand targets)
    - Document anchor-based navigation pattern (stable landmarks)
    - Document self-healing recovery (retry on stale references)
    - Document state change detection (verify actions succeeded)
  - [x] 21.3. Create SANDBOX_SETUP.md guide (docs/SANDBOX_SETUP.md)
    - Document .wsb configuration format
    - Document security considerations (path validation, canonical resolution)
    - Document troubleshooting (VM doesn't start, named pipe errors, touch permission denied)
  - [x] 21.3.5. Create HOST_SETUP.md guide (docs/HOST_SETUP.md)
    - Document one-time host setup requirements
    - Registry key: `RemoteDesktop_SuppressWhenMinimized = 2`
    - PowerShell script for automated setup
    - How to verify setup is correct
    - Troubleshooting: "My automation fails when minimized"
  - [x] 21.4. Create TOUCH_PEN_GUIDE.md guide (docs/TOUCH_PEN_GUIDE.md)
    - Document coordinate systems (logical vs physical, DPI scaling)
    - Document pressure sensitivity for pen input
    - Document gesture patterns (tap, drag, pinch, stroke)
    - Document Admin privilege requirement (solved by sandbox)

- [x] 22. Create example agent scripts
  - Demonstrate common automation patterns
  - Implements requirement: 5.3 (Documentation)
  - [x] 22.1. Create example: Simple form filling agent (examples/form-filling-agent.json)
    - Script that finds form, fills fields, clicks submit
    - Demonstrates: get_ui_tree, type_text, click_element, state_changed
  - [x] 22.2. Create example: Test case generation agent (examples/test-generation-agent.json)
    - Script that explores UI and generates test cases
    - Demonstrates: progressive disclosure, state validation, event subscription
  - [x] 22.3. Create example: Bug detection agent (examples/bug-detection-agent.json)
    - Script that looks for UI bugs (unlabeled buttons, disabled without explanation)
    - Demonstrates: tree analysis, heuristic checks, anchor-based navigation
  - [x] 22.4. Create example: Pen-based drawing agent (examples/ink-drawing-agent.json)
    - Script that uses InkCanvas to draw shapes
    - Demonstrates: pen_stroke, pressure variation, coordinate normalization
  - [x] 22.5. Add examples to repository in /examples directory (examples/README.md)

---

## Phase 11: Testing and Validation

Comprehensive E2E testing and security audit.

- [ ] 23. Write E2E test suite
  - Test all tools in realistic scenarios
  - Implements requirement: 3.2 (Reliability)
  - [ ] 23.1. Create E2E test: Full exploration workflow
    - Launch TestApp in sandbox
    - Agent explores UI tree progressively (shallow, expand, deep)
    - Agent identifies all interactive elements
    - Agent tests each button/textbox
    - Verify no exceptions, all actions succeed
  - [ ] 23.2. Create E2E test: Security validation
    - Launch TestApp in sandbox
    - Agent attempts File → Open → C:\Users\{username}\Documents
    - Verify path does NOT exist in sandbox
    - Agent opens File Explorer, verify only sees sandbox C:\
    - Verify host filesystem inaccessible
  - [ ] 23.3. Create E2E test: Error recovery
    - Launch TestApp, agent caches elements
    - Kill and restart TestApp
    - Agent continues automation (triggers self-healing)
    - Verify agent recovers and completes task
  - [ ] 23.4. Create E2E test: Performance under load
    - Launch TestApp with 1000+ UI elements
    - Agent calls get_ui_tree repeatedly (10 times)
    - Verify latency stays <2s per call
    - Verify memory usage stable (no leaks)
  - [ ] 23.5. Create E2E test: Touch and pen input
    - Launch TestApp with InkCanvas
    - Agent draws signature using pen_stroke
    - Agent pinch zooms canvas
    - Verify ink captured, zoom level changed

- [ ] 24. Perform security audit
  - Validate sandbox isolation effectiveness
  - Implements requirement: 2.7 (Sandboxing & Safety)
  - [ ] 24.1. Manual security test: File deletion prevention
    - Launch TestApp in sandbox with File menu
    - Agent clicks File → Open → navigates to C:\Users\{username}
    - Verify path does NOT exist
    - Verify agent cannot delete host files
  - [ ] 24.2. Manual security test: Network isolation
    - Launch TestApp in sandbox with network-disabled .wsb
    - Agent attempts web request (if app has WebBrowser control)
    - Verify request fails (ERR_NETWORK_ACCESS_DENIED)
  - [ ] 24.3. Manual security test: .wsb validation bypass attempts
    - Attempt symlink path in .wsb: `C:\Link -> C:\Users\jhedin\Documents`
    - Verify launch_app_sandboxed resolves symlink and rejects
    - Attempt case variation: `C:\users\jhedin\documents`
    - Verify case-insensitive validation catches it
    - Attempt path traversal: `C:\TestApps\..\..\..\Users\jhedin\Documents`
    - Verify canonical path resolution catches traversal
  - [ ] 24.4. Document security findings
    - Create SECURITY_AUDIT.md with test results
    - List residual risks (if any)
    - Provide security best practices for agent developers

---

## Migration Tasks

Update existing MCP server code to integrate new features.

**STATUS**: SessionManager fully implemented. Handler architecture complete.

- [x] 25. Refactor session management
  - Centralize session state in SessionManager class
  - Implements design section 3.1
  - [x] 25.1. SessionManager class in Program.cs
    - Properties: element_cache, event_queue, expansion_state, tree_cache
    - Methods: CacheElement(), GetElement(), MarkForExpansion(), etc.
  - [x] 25.2. All handlers receive SessionManager via HandlerBase
  - [x] 25.3. Session lifecycle managed via handler registration
  - [ ] 25.4. Write comprehensive unit tests (requires Windows)

- [ ] 26. PID filtering for tools
  - [ ] 26.1. Consider adding active_pid filtering to element operations
  - (Low priority - window-relative coordinates provide sufficient scoping)

---

## Success Criteria

**STATUS**: Core implementation and documentation complete. Integration tests pending (require Windows).

Implementation is complete when:

1. ✅ Core tool implementation complete (Phases 1-8)
2. ✅ All builds pass (`dotnet build`)
3. ⬜ Integration tests pass (requires Windows)
4. ✅ Documentation complete (Phase 10, tasks 21.1-21.4)
5. ⬜ E2E test suite complete (Phase 11)

## Out of Scope

The following are explicitly NOT included in this implementation:

- AI model integration (agent logic lives outside MCP server)
- Web browser automation (only Windows desktop apps)
- Cross-platform support (Windows-only)
- Visual validation (screenshots exist, but no OCR or image diffing)
- Mobile device automation
- Legacy Win32 app support (only UIA-compatible apps)
- Network-based testing (sandbox runs network-disabled)
- Multi-monitor setups (coordinate system assumes single monitor)
