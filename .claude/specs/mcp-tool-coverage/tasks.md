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
4. **Avoid UNC paths in PowerShell cd**: Copy binaries to Windows-native paths (e.g., `C:\TransportTest\`) to avoid path issues.

**Test Procedure** (see `prototypes/transport-test/README.md` for details):
1. Build with `dotnet publish ... --self-contained true -o C:\TransportTest\...`
2. Run host: `C:\TransportTest\Host\SharedFolderHost.exe C:\TransportTest\Shared`
3. Launch sandbox from admin PowerShell: `WindowsSandbox.exe C:\TransportTest\test-shared-folder.wsb`
4. Check `C:\TransportTest\Shared\client-ready.signal` appears

---

## Phase 0.5: VDD Integration & Background Automation

Enable background automation by integrating Virtual Display Driver.

**Prerequisites**: Host must have registry key set (one-time setup, documented in requirements.md 1.1).

- [ ] 1.5. Bundle VDD driver with MCP server
  - Select and integrate a Virtual Display Driver for background automation
  - [ ] 1.5.1. Research and select VDD driver
    - Evaluate IddSampleDriver (Microsoft sample) - open source, demonstrates IddCx framework
    - Evaluate parsec-vdd or similar community drivers
    - Criteria: UMDF-based, minimal dependencies, permissive license, Windows 10/11 compatible
    - Document selection rationale in `research-vdd-selection.md`
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

### ⚠️ BLOCKER: Windows Sandbox Regression in Build 26200 (2026-01-13)

**Status**: E2E testing blocked due to **known Microsoft regression** in Windows 11 25H2 build 26200.

**Root Cause** (confirmed):
- `C:\ProgramData\Microsoft\Windows\Containers\BaseImages` directory is **empty**
- This is a known regression introduced in KB5070311 or earlier
- Sandbox cannot initialize without base images
- Multiple users affected across builds 26200.7462, 26200.7623, etc.
- Reference: [ElevenForum thread](https://www.elevenforum.com/t/windows-sandbox-just-spinning-at-start-25h2-26200-7462.43489/)

**Symptoms**:
- Sandbox hangs with spinning logo, then error 0x800705B4 (timeout)
- Or crashes with BSOD: `SYSTEM_SERVICE_EXCEPTION (0x3B)` / `ACCESS_VIOLATION (0xC0000005)`
- Basic `WindowsSandbox.exe` (no config) also fails
- Hyper-V and WSL continue to work normally

**Environment**:
- Windows 11 Pro 25H2 (Build 10.0.26200)
- Windows Sandbox version 0.5.3.0
- Hyper-V Host Compute Service (vmcompute) running
- BaseImages directory confirmed empty

**What does NOT fix it**:
- ❌ Disable/re-enable Windows Sandbox (doesn't recreate BaseImages)
- ❌ SFC/DISM scans (no corruption found)
- ❌ Reinstalling the Sandbox feature
- ❌ Restarting vmcompute service

**Potential workarounds**:
1. Check for newer cumulative Windows updates
2. Roll back to a previous Windows build (if available)
3. Wait for Microsoft to release a fix
4. Consider testing on a different machine with stable Sandbox

**Note**: Phase 0 transport testing was successful earlier (before BSOD). The shared folder polling transport is confirmed working when sandbox is stable.

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

- [ ] 10. Implement `touch_tap` tool
  - Simulate touch tap at coordinates
  - Implements requirement: 2.10.1 (inferred from user input)
  - [ ] 10.1. Write unit tests for TouchInjector
    - Test coordinate validation (in-bounds check)
    - Test DPI scaling (logical → physical coordinates)
    - Test error handling (Admin privileges required)
  - [ ] 10.2. Implement TouchInjector.InjectTap() method
    - Accept logical coordinates (x, y)
    - Convert to physical coordinates using DPI scale factor
    - Call `User32.InjectTouchInput()` with POINTER_TOUCH_INFO:
      - pointerType = PT_TOUCH
      - contactArea = small circle (5x5 pixels)
      - pressure = 512 (medium)
    - Inject sequence: POINTER_DOWN → POINTER_UP
    - Wait 50ms between events
    - Return success/failure
  - [ ] 10.3. Add touch_tap to MCP tool registry
    - Accept parameters: x, y (logical coordinates)
    - Return: success, dpi_scale_factor
  - [ ] 10.4. Write integration test
    - Launch TestApp with button at known position
    - Touch tap button coordinates
    - Verify button click event fired

- [ ] 10. Implement `touch_drag` tool
  - Simulate touch drag gesture
  - Implements requirement: 2.10.2 (inferred)
  - [ ] 10.1. Write unit tests for drag gesture
    - Test straight line drag
    - Test multi-step interpolation (smooth movement)
  - [ ] 10.2. Implement TouchInjector.InjectDrag() method
    - Accept start (x1, y1), end (x2, y2), steps (default 10)
    - Interpolate path: `xi = x1 + (x2-x1) * i / steps`
    - Inject sequence:
      1. POINTER_DOWN at (x1, y1)
      2. Loop steps: POINTER_UPDATE at (xi, yi) with 20ms delay
      3. POINTER_UP at (x2, y2)
    - Return success/failure
  - [ ] 10.3. Add touch_drag to MCP tool registry
  - [ ] 10.4. Write integration test
    - Drag slider thumb from left to right
    - Verify slider value changed

- [ ] 11. Implement `pen_stroke` tool
  - Simulate pen stroke with pressure
  - Implements requirement: 2.10.3 (inferred)
  - [ ] 11.1. Write unit tests for PenInjector
    - Test pressure variation (0-1024 range)
    - Test eraser mode (inverted flag)
  - [ ] 11.2. Implement PenInjector.InjectStroke() method
    - Accept path points: `[(x, y, pressure), ...]`
    - Call `InjectTouchInput()` with POINTER_PEN_INFO:
      - pointerType = PT_PEN
      - pressure = per-point value (0-1024)
      - penFlags = PEN_FLAG_BARREL (if eraser mode)
    - Inject sequence: POINTER_DOWN → POINTER_UPDATE (each point) → POINTER_UP
    - Return success/failure
  - [ ] 11.3. Add pen_stroke to MCP tool registry
    - Accept parameters: points array, eraser (bool)
  - [ ] 11.4. Write integration test with InkCanvas
    - Draw stroke on InkCanvas control
    - Verify ink appears (via screenshot comparison or stroke count)

- [ ] 12. Implement `pinch_zoom` tool
  - Simulate two-finger pinch gesture
  - Implements requirement: 2.10.4 (inferred)
  - [ ] 12.1. Write unit tests for multi-touch
    - Test two simultaneous touch points
    - Test distance calculation (start vs end)
  - [ ] 12.2. Implement TouchInjector.InjectPinch() method
    - Accept center (x, y), startDistance, endDistance, steps
    - Compute two finger positions:
      - finger1: (x - dist/2, y)
      - finger2: (x + dist/2, y)
    - Inject parallel touch sequences for both fingers
    - Interpolate distance from startDistance to endDistance
    - Return success/failure
  - [ ] 12.3. Add pinch_zoom to MCP tool registry
  - [ ] 12.4. Write integration test
    - Pinch zoom on image control
    - Verify zoom level changed

---

## Phase 5: DPI and Coordinate Normalization

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

- [ ] 14. Implement `subscribe_to_events` tool
  - Push UI events to agent
  - Implements requirement: 2.5.1
  - [ ] 14.1. Write unit tests for EventQueue
    - Test event buffering (max 100 events, FIFO eviction)
    - Test event filtering (by AutomationId, ControlType)
  - [ ] 14.2. Implement AutomationEventListener class
    - Register handlers:
      - `Automation.AddAutomationPropertyChangedEventHandler`
      - `Automation.AddStructureChangedEventHandler`
    - Filter events by session active_pid
    - Push events to SessionManager.event_queue
  - [ ] 14.3. Add subscribe_to_events to MCP tool registry
    - Accept filter: element_id, event_types (PropertyChanged, StructureChanged)
    - Return subscription_id
  - [ ] 14.4. Implement `get_events` tool for polling
    - Accept subscription_id
    - Return events since last poll (array of event objects)
    - Clear returned events from queue
  - [ ] 14.5. Write integration test
    - Subscribe to PropertyChanged on TextBox
    - Type text into TextBox (via separate action)
    - Poll events, verify Text property changed

---

## Phase 7: Advanced Navigation

Implement progressive disclosure and anchor-based search.

- [ ] 15. Implement progressive disclosure
  - Start with shallow tree, expand on demand
  - Implements requirement: 2.5.2
  - [ ] 15.1. Write unit tests for expansion tracking
    - Test SessionManager.expansion_state dictionary
    - Test TreeBuilder respects expansion_state
  - [ ] 15.2. Modify SessionManager to track expanded elements
    - Add Dictionary<elementId, isExpanded>
    - Add MarkExpanded(elementId) method
  - [ ] 15.3. Modify TreeBuilder to check expansion state
    - When traversing tree, check if element is marked for expansion
    - Only include children if element is expanded OR depth < max_depth
  - [ ] 15.4. Add `mark_for_expansion` tool
    - Accept elementId
    - Mark element in SessionManager.expansion_state
    - Return success (next get_ui_tree will include subtree)
  - [ ] 15.5. Write E2E test
    - Call get_ui_tree with max_depth=1 (shallow)
    - Identify target container, call mark_for_expansion
    - Call get_ui_tree again, verify subtree visible

- [ ] 16. Implement anchor-based navigation
  - Find elements relative to stable landmarks
  - Implements design section 3.2.3
  - [ ] 16.1. Write unit tests for anchor search
    - Test FindNearestAnchor(element) returns labeled parent
    - Test RelativeLocator("Submit button", anchor="Login Form")
  - [ ] 16.2. Implement AutomationHelper.FindByAnchor() method
    - Accept anchor_name (e.g., "Login Form")
    - Accept target_name (e.g., "Submit button")
    - Find anchor element by Name or AutomationId
    - Search anchor's descendants for target
    - Return target element or error with suggestions
  - [ ] 16.3. Add find_by_anchor to MCP tool registry
  - [ ] 16.4. Write integration test
    - Find "Save" button relative to "User Profile Form"
    - Verify correct button found (not "Save" in different form)

---

## Phase 8: Self-Healing and Robustness

Implement error recovery for stale element references.

- [ ] 17. Implement self-healing element location
  - Recover from stale references
  - Implements design section 4.2
  - [ ] 17.1. Write unit tests for stale element detection
    - Test ElementNotFoundException triggers re-search
    - Test InvalidOperationException triggers re-search
  - [ ] 17.2. Implement AutomationHelper.RelocateElement() method
    - Accept stale elementId + original search criteria (AutomationId, Name)
    - Re-run find operation with same criteria
    - If found, update element cache with new reference
    - Return success/failure with suggestions
  - [ ] 17.3. Modify all action methods to catch stale element errors
    - Wrap click_element, type_text, etc. in try-catch
    - On ElementNotFoundException:
      1. Log warning: "Element stale, attempting relocation"
      2. Call RelocateElement() with original criteria
      3. Retry action once with new reference
      4. If second attempt fails, return structured error
  - [ ] 17.4. Write integration test
    - Cache button element
    - Close and reopen TestApp (stale reference)
    - Attempt click on stale element
    - Verify self-healing relocates button and completes click

- [ ] 18. Implement `get_capabilities` tool
  - Report MCP server feature support
  - Implements requirement: 2.1.5
  - [ ] 18.1. Write unit tests for capability reporting
    - Test returns supported_tools array
    - Test returns max_tree_depth, max_tokens_per_tree
    - Test returns dpi_aware, touch_supported flags
  - [ ] 18.2. Implement ServerCapabilities data structure
    - Static capabilities: FlaUI version, UIA2 backend
    - Runtime capabilities: sandbox_available, OS version, DPI scale factor
    - Supported tools list with implementation status
  - [ ] 18.3. Add get_capabilities to MCP tool registry
  - [ ] 18.4. Document capabilities in README

---

## Phase 9: Performance and Optimization

Optimize for large UI trees and high-frequency calls.

- [ ] 19. Optimize tree building performance
  - Reduce latency for large trees
  - Implements non-functional requirement: 3.1 (Performance)
  - [ ] 19.1. Write performance benchmarks
    - Measure get_ui_tree latency for 10/100/500/1000 element trees
    - Target: <500ms for typical windows, <2s for complex windows
  - [ ] 19.2. Implement tree caching with dirty detection
    - Cache last tree result with timestamp
    - Subscribe to StructureChanged events to mark tree dirty
    - Return cached tree if not dirty and <5s old
    - Cache hit rate target: >70% for repeated calls
  - [ ] 19.3. Implement parallel property fetching
    - Use Task.WhenAll to fetch AutomationId, Name, IsEnabled in parallel
    - Reduces per-element overhead from ~15ms to ~5ms
  - [ ] 19.4. Re-run benchmarks, verify improvement
    - Target: 50% latency reduction for large trees

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

- [ ] 21. Write MCP tool documentation
  - Document all tools with examples
  - Implements requirement: 5.3 (Documentation)
  - [ ] 21.1. Create MCP_TOOLS.md reference
    - For each tool:
      - Name, description
      - Parameters (type, required, default)
      - Return value (structure, examples)
      - Error codes and recovery suggestions
    - Include tool dependency graph (which tools require others)
  - [ ] 21.2. Create AGENT_EXPLORATION_GUIDE.md usage guide
    - Document OODA loop workflow (Observe, Orient, Decide, Act)
    - Document progressive disclosure pattern (shallow first, expand targets)
    - Document anchor-based navigation pattern (stable landmarks)
    - Document self-healing recovery (retry on stale references)
    - Document state change detection (verify actions succeeded)
  - [ ] 21.3. Create SANDBOX_SETUP.md guide
    - Document .wsb configuration format
    - Document security considerations (path validation, canonical resolution)
    - Document troubleshooting (VM doesn't start, named pipe errors, touch permission denied)
  - [ ] 21.3.5. Create HOST_SETUP.md guide
    - Document one-time host setup requirements
    - Registry key: `RemoteDesktop_SuppressWhenMinimized = 2`
    - PowerShell script for automated setup
    - How to verify setup is correct
    - Troubleshooting: "My automation fails when minimized"
  - [ ] 21.4. Create TOUCH_PEN_GUIDE.md guide
    - Document coordinate systems (logical vs physical, DPI scaling)
    - Document pressure sensitivity for pen input
    - Document gesture patterns (tap, drag, pinch, stroke)
    - Document Admin privilege requirement (solved by sandbox)

- [ ] 22. Create example agent scripts
  - Demonstrate common automation patterns
  - Implements requirement: 5.3 (Documentation)
  - [ ] 22.1. Create example: Simple form filling agent
    - Script that finds form, fills fields, clicks submit
    - Demonstrates: get_ui_tree, type_text, click_element, state_changed
  - [ ] 22.2. Create example: Test case generation agent
    - Script that explores UI and generates test cases
    - Demonstrates: progressive disclosure, state validation, event subscription
  - [ ] 22.3. Create example: Bug detection agent
    - Script that looks for UI bugs (unlabeled buttons, disabled without explanation)
    - Demonstrates: tree analysis, heuristic checks, anchor-based navigation
  - [ ] 22.4. Create example: Pen-based drawing agent
    - Script that uses InkCanvas to draw shapes
    - Demonstrates: pen_stroke, pressure variation, coordinate normalization
  - [ ] 22.5. Add examples to repository in /examples directory

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

- [ ] 25. Update existing tools for PID filtering
  - Modify all tools to respect session active_pid
  - Implements requirement: 2.6.1
  - [ ] 25.1. Modify find_element to filter by PID
    - Before returning elements, check window PID
    - If active_pid set, skip windows from other processes
  - [ ] 25.2. Modify click_element to validate PID
    - Before clicking, verify element belongs to active_pid
    - Return error if element is from different process
  - [ ] 25.3. Modify type_text, set_value similarly
  - [ ] 25.4. Update all existing tools (drag_drop, send_keys, etc.)

- [ ] 26. Refactor session management
  - Centralize session state in SessionManager class
  - Implements design section 3.1
  - [ ] 26.1. Create SessionManager class
    - Properties: active_pid, element_cache, event_queue, expansion_state, sandbox_handles
    - Methods: CacheElement(), GetCachedElement(), ClearSession(), MarkExpanded()
  - [ ] 26.2. Modify Program.cs to use SessionManager
    - Replace global dictionaries with SessionManager instance
    - Pass SessionManager to all tool handlers
  - [ ] 26.3. Add session lifecycle management
    - Initialize session on first tool call
    - Clear session on close_sandbox or explicit reset
  - [ ] 26.4. Write unit tests for SessionManager
    - Test element caching (add, retrieve, expiration after 60s)
    - Test PID tracking
    - Test session reset clears all state

---

## Success Criteria

Implementation is complete when:

1. ✅ All 29 task groups above are checked off (including Phase 0.5 VDD tasks)
2. ✅ All unit tests pass (dotnet test)
3. ✅ All integration tests pass
4. ✅ All E2E tests pass (including security tests)
5. ✅ Documentation complete:
   - MCP_TOOLS.md (tool reference)
   - AGENT_EXPLORATION_GUIDE.md (usage patterns)
   - SANDBOX_SETUP.md (security and troubleshooting)
   - TOUCH_PEN_GUIDE.md (input injection)
   - SECURITY_AUDIT.md (test results)
   - HOST_SETUP.md (registry key configuration, one-time setup)
6. ✅ Security audit passed with no critical findings
7. ✅ Performance benchmarks met (<500ms typical, <2s complex trees)
8. ✅ Example agent scripts run successfully (4 examples)
9. ✅ Touch/pen input tested on real pen-enabled device (InkCanvas use case)
10. ✅ DPI scaling tested at 100%, 125%, 150%, 175%, 200%
11. ✅ Background automation tested (sandbox minimized, automation continues)
12. ✅ VDD driver bundled and installs correctly in sandbox bootstrap

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
