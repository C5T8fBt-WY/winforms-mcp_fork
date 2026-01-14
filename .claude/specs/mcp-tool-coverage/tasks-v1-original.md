# MCP Tool Coverage - Implementation Tasks

## Phase 0: Research Spike (BLOCKING)

These tasks MUST be completed before proceeding with implementation. They validate fundamental assumptions about Windows Sandbox architecture.

- [ ] 1. Research Windows Sandbox + UIA automation feasibility
  - Research whether FlaUI/UIA can cross VM boundary
  - Implements requirement: 2.7 (Sandboxing & Safety)
  - [ ] 1.1. Create minimal Windows Sandbox test
    - Create test.wsb configuration with basic settings
    - Launch sandbox programmatically via `WindowsSandbox.exe test.wsb`
    - Verify sandbox starts successfully
  - [ ] 1.2. Test UIA automation from host → sandbox VM
    - Launch simple WinForms app inside sandbox (e.g., Notepad)
    - From host process, attempt `Automation.GetDesktop()` and search for sandbox windows
    - Document whether UIA can see/interact with apps inside sandbox VM
  - [ ] 1.3. Test UIA automation inside sandbox → sandbox apps
    - Build standalone MCP server executable
    - Copy MCP server into sandbox via LogonCommand in .wsb
    - Launch target app inside sandbox
    - Run MCP server inside sandbox, test if it can automate the app
    - Document stdio communication challenges (if any)

**DECISION POINT**: Based on research results, choose architecture:
- **Option A**: UIA works across VM boundary → MCP runs on host (proceed to Phase 1)
- **Option B**: UIA requires same VM → MCP runs inside sandbox (proceed to Phase 2)
- **Option C**: Neither works → Fallback to Docker/Hyper-V (Phase 3)

---

## Phase 1: Core Observation Tools (MCP on Host Architecture)

**Prerequisites**: Research spike confirms Option A (UIA crosses VM boundary)

- [ ] 2. Implement `get_ui_tree` tool
  - Return hierarchical UI tree with configurable depth
  - Implements requirement: 2.1.1
  - [ ] 2.1. Write unit tests for TreeBuilder class
    - Test depth limiting (max_depth=1 returns only root, max_depth=3 returns 3 levels)
    - Test heuristic pruning (skip invisible/disabled elements)
    - Test token count calculation (verify <5000 tokens for typical windows)
  - [ ] 2.2. Implement TreeBuilder.BuildTree() method
    - Create recursive tree traversal respecting max_depth parameter
    - Apply heuristic filters (skip non-visible, skip AutomationId="PART_*")
    - Return XML string with AutomationId, Name, ControlType, IsEnabled
  - [ ] 2.3. Add get_ui_tree to MCP tool registry
    - Modify Program.cs to register new tool handler
    - Add JSON-RPC request/response handling
    - Add PID filtering (only show windows matching session's active_pid)
  - [ ] 2.4. Write integration tests for get_ui_tree
    - Launch TestApp.exe with known UI structure
    - Call get_ui_tree with max_depth=2
    - Verify returned XML matches expected structure

- [ ] 3. Implement `expand_collapse` tool
  - Toggle ExpandCollapse pattern elements (TreeView, Accordion)
  - Implements requirement: 2.2.1
  - [ ] 3.1. Write unit tests for ExpandCollapseAction
    - Test expand on collapsed element (verify ExpandCollapseState.Expanded)
    - Test collapse on expanded element (verify ExpandCollapseState.Collapsed)
    - Test error handling for elements without ExpandCollapse pattern
  - [ ] 3.2. Implement AutomationHelper.ExpandCollapseElement() method
    - Accept elementId (cached element reference)
    - Get ExpandCollapsePattern from element
    - Call Expand() or Collapse() based on desired state
    - Return success/failure with current state
  - [ ] 3.3. Add expand_collapse to MCP tool registry
    - Add JSON-RPC handler in Program.cs
    - Integrate with SessionManager element cache
  - [ ] 3.4. Write integration test with TreeView control
    - Launch TestApp with TreeView containing nested items
    - Expand root node, verify children visible in subsequent get_ui_tree

- [ ] 4. Implement `scroll` tool
  - Scroll scrollable containers (ScrollPattern)
  - Implements requirement: 2.3.1
  - [ ] 4.1. Write unit tests for ScrollAction
    - Test vertical scroll (ScrollAmount.SmallIncrement, LargeIncrement)
    - Test horizontal scroll
    - Test error handling for non-scrollable elements
  - [ ] 4.2. Implement AutomationHelper.ScrollElement() method
    - Accept elementId, direction (vertical/horizontal), amount (small/large/page)
    - Get ScrollPattern from element
    - Call Scroll() with appropriate ScrollAmount enum
    - Return new scroll position (percentage)
  - [ ] 4.3. Add scroll to MCP tool registry
  - [ ] 4.4. Write integration test with long ListBox
    - Launch TestApp with ListBox containing 100+ items
    - Scroll down, verify new items visible via get_ui_tree

- [ ] 5. Implement `check_element_state` tool
  - Query individual element properties without full tree
  - Implements requirement: 2.4.1
  - [ ] 5.1. Write unit tests for StateQuery
    - Test enabled/disabled detection
    - Test visibility detection
    - Test value retrieval (TextBox, Slider current values)
  - [ ] 5.2. Implement AutomationHelper.GetElementState() method
    - Accept elementId or AutomationId string
    - Return { isEnabled, isVisible, value, controlType, boundingRect }
  - [ ] 5.3. Add check_element_state to MCP tool registry
  - [ ] 5.4. Write integration test with dynamic UI
    - Launch TestApp with button that enables/disables textbox
    - Click button, verify textbox state changes via check_element_state

- [ ] 6. Implement `get_process_info` tool
  - Return active processes and establish session PID scope
  - Implements requirement: 2.6.1
  - [ ] 6.1. Write unit tests for ProcessManager
    - Test GetRunningProcesses() returns list of { pid, name, windowTitle }
    - Test SetActivePID() updates SessionManager.active_pid
  - [ ] 6.2. Implement AutomationHelper.GetAllWindows() method
    - Enumerate all top-level windows via Automation.GetDesktop()
    - For each window, get PID via Win32 GetWindowThreadProcessId
    - Return list of { pid, processName, windowTitle, windowHandle }
  - [ ] 6.3. Add get_process_info to MCP tool registry
    - Add optional set_active_pid parameter
    - When set_active_pid provided, update session state
    - All subsequent get_ui_tree calls filter by active_pid
  - [ ] 6.4. Write integration test for PID filtering
    - Launch two TestApp instances (PID1, PID2)
    - Call get_process_info and set_active_pid=PID1
    - Verify get_ui_tree only returns windows from PID1

---

## Phase 2: Sandbox Integration (MCP Inside Sandbox Architecture)

**Prerequisites**: Research spike confirms Option B (MCP must run inside sandbox)

- [ ] 7. Implement MCP server transport for sandbox
  - Enable stdio communication across host/sandbox boundary
  - Implements requirement: 2.7.1
  - [ ] 7.1. Research stdio transport options
    - Test named pipes from host → sandbox
    - Test TCP localhost from host → sandbox
    - Test file-based request/response via mapped folder
    - Document latency and reliability of each approach
  - [ ] 7.2. Implement chosen transport in Program.cs
    - Modify stdin/stdout handlers to use selected mechanism
    - Ensure JSON-RPC messages preserve line boundaries
  - [ ] 7.3. Write integration test for cross-VM communication
    - Launch sandbox with MCP server inside
    - Send JSON-RPC request from host script
    - Verify response received and valid

- [ ] 8. Implement `launch_app_sandboxed` tool
  - Start Windows Sandbox with target app
  - Implements requirement: 2.7.1
  - [ ] 8.1. Write unit tests for SandboxManager
    - Test .wsb generation from template
    - Test .wsb validation (reject sensitive paths)
    - Test sandbox launch via Process.Start("WindowsSandbox.exe")
  - [ ] 8.2. Implement SandboxManager.LaunchSandbox() method
    - Accept app_path (application to automate)
    - Generate .wsb configuration:
      - Networking disabled
      - vGPU disabled
      - Mapped folder for app_path (read-only)
      - LogonCommand to launch app + MCP server
    - Validate .wsb does NOT map sensitive folders
    - Launch sandbox via WindowsSandbox.exe
    - Return sandbox_id (handle/PID tracking)
  - [ ] 8.3. Implement .wsb validation rules
    - Reject mappings to C:\Users\{username}\Documents
    - Reject mappings to C:\Users\{username}\Desktop
    - Reject mappings to system folders (Windows, Program Files)
    - Only allow mappings to explicit test data directories
  - [ ] 8.4. Add launch_app_sandboxed to MCP tool registry
  - [ ] 8.5. Write E2E test for sandboxed automation
    - Call launch_app_sandboxed with TestApp.exe
    - Verify sandbox starts, app launches
    - Call get_ui_tree, verify TestApp UI returned
    - Attempt to open File Explorer inside sandbox
    - Verify C:\Users\jhedin\ does NOT exist in sandbox

- [ ] 9. Implement `close_sandbox` tool
  - Gracefully shutdown sandbox VM
  - Implements requirement: 2.7.4
  - [ ] 9.1. Write unit tests for sandbox cleanup
    - Test graceful shutdown (terminate MCP server first)
    - Test force shutdown (kill sandbox process)
    - Test resource cleanup (handles released)
  - [ ] 9.2. Implement SandboxManager.CloseSandbox() method
    - Accept sandbox_id
    - Send shutdown signal to MCP server inside sandbox
    - Wait for graceful exit (timeout: 10s)
    - If timeout, force kill sandbox process
    - Clean up session state (remove sandbox_id from active list)
  - [ ] 9.3. Add close_sandbox to MCP tool registry
  - [ ] 9.4. Write integration test for cleanup
    - Launch sandbox with TestApp
    - Close sandbox via tool
    - Verify Windows Sandbox.exe process terminated
    - Verify no orphaned child processes

---

## Phase 3: Event System

**Prerequisites**: Phase 1 or Phase 2 complete (depending on architecture)

- [ ] 10. Implement `subscribe_to_events` tool
  - Push UI events to agent (PropertyChanged, StructureChanged)
  - Implements requirement: 2.5.1
  - [ ] 10.1. Write unit tests for EventQueue
    - Test event buffering (max 100 events, FIFO eviction)
    - Test event filtering (by AutomationId, ControlType)
    - Test event serialization to JSON
  - [ ] 10.2. Implement AutomationEventListener class
    - Register for Automation.AddAutomationPropertyChangedEventHandler
    - Register for Automation.AddStructureChangedEventHandler
    - Filter events by session's active_pid
    - Push events to SessionManager.event_queue
  - [ ] 10.3. Add subscribe_to_events to MCP tool registry
    - Accept filter parameters (element_id, event_types)
    - Return subscription_id
  - [ ] 10.4. Implement event polling mechanism
    - Add get_events tool to retrieve queued events
    - Accept subscription_id, return array of events since last poll
  - [ ] 10.5. Write integration test for event capture
    - Launch TestApp with dynamic UI (button enables textbox)
    - Subscribe to PropertyChanged events on textbox
    - Click button
    - Poll events, verify IsEnabled changed event received

---

## Phase 4: Advanced Navigation

**Prerequisites**: Phase 3 complete

- [ ] 11. Implement progressive disclosure strategy
  - Start with shallow tree, expand on demand
  - Implements requirement: 2.5.2
  - [ ] 11.1. Write unit tests for tree expansion logic
    - Test get_ui_tree with max_depth=1 returns top-level elements
    - Test expand_element marks element as "needs_expansion"
    - Test subsequent get_ui_tree includes expanded subtree
  - [ ] 11.2. Implement SessionManager.expansion_state tracking
    - Add Dictionary<elementId, isExpanded> to session
    - Modify TreeBuilder to check expansion_state
    - Only traverse children if element is expanded
  - [ ] 11.3. Add expand_element action (different from expand_collapse)
    - This is for tree navigation, not UI interaction
    - Marks element for inclusion in next get_ui_tree
  - [ ] 11.4. Write E2E test for progressive disclosure
    - Launch TestApp with complex nested UI
    - Call get_ui_tree with max_depth=1 (few tokens)
    - Identify target container, call expand_element
    - Call get_ui_tree again, verify subtree now visible

- [ ] 12. Implement anchor-based navigation
  - Find elements relative to stable landmarks
  - Implements design section 3.2.3
  - [ ] 12.1. Write unit tests for anchor search
    - Test FindNearestAnchor(element) returns closest labeled parent
    - Test RelativeLocator("Submit button", anchor="Login Form")
  - [ ] 12.2. Implement AutomationHelper.FindByAnchor() method
    - Accept anchor_name (e.g., "Login Form"), target_name (e.g., "Submit button")
    - Find anchor element by Name or AutomationId
    - Search anchor's descendants for target
    - Return target element or error with suggestions
  - [ ] 12.3. Add find_by_anchor to MCP tool registry
  - [ ] 12.4. Write integration test with complex form
    - Launch TestApp with multiple forms
    - Find "Save" button relative to "User Profile Form" anchor
    - Verify correct button found (not "Save" in different form)

---

## Phase 5: Self-Healing and Robustness

**Prerequisites**: Phase 4 complete

- [ ] 13. Implement self-healing element location
  - Recover from stale element references
  - Implements design section 4.2
  - [ ] 13.1. Write unit tests for stale element detection
    - Test ElementNotFoundException triggers re-search
    - Test InvalidOperationException triggers re-search
  - [ ] 13.2. Implement AutomationHelper.RelocateElement() method
    - Accept stale elementId + original search criteria
    - Re-run find operation with same AutomationId/Name
    - If found, update element cache with new reference
    - Return success/failure with suggestions if not found
  - [ ] 13.3. Modify all action methods to catch stale element errors
    - Wrap click_element, type_text, etc. in try-catch
    - On ElementNotFoundException, attempt RelocateElement()
    - Retry action once with new reference
    - If second attempt fails, return structured error
  - [ ] 13.4. Write integration test for self-healing
    - Launch TestApp, cache button element
    - Close and reopen TestApp (stale reference)
    - Attempt click on stale element
    - Verify self-healing relocates button and completes click

- [ ] 14. Implement get_capabilities tool
  - Return MCP server feature support flags
  - Implements requirement: 2.1.5
  - [ ] 14.1. Write unit tests for capability reporting
    - Test returns supported_tools array
    - Test returns max_tree_depth, max_tokens_per_tree
    - Test returns sandbox_available boolean
  - [ ] 14.2. Implement ServerCapabilities data structure
    - Define static capabilities (FlaUI version, UIA2 backend)
    - Define runtime capabilities (sandbox available, OS version)
  - [ ] 14.3. Add get_capabilities to MCP tool registry
    - Return JSON with all capability flags
  - [ ] 14.4. Document capabilities in MCP README
    - List all tools with status (implemented/planned)
    - Note platform requirements (Windows 10+, .NET 8)

---

## Phase 6: Performance and Optimization

**Prerequisites**: Phase 5 complete

- [ ] 15. Optimize tree building performance
  - Reduce latency for large UI trees
  - Implements non-functional requirement: 3.1 (Performance)
  - [ ] 15.1. Write performance benchmarks
    - Measure get_ui_tree latency for 10/100/1000 element trees
    - Target: <500ms for typical windows, <2s for complex windows
  - [ ] 15.2. Implement tree caching with dirty detection
    - Cache last tree result with timestamp
    - Subscribe to StructureChanged events to mark tree dirty
    - Return cached tree if not dirty and <5s old
  - [ ] 15.3. Implement parallel element property fetching
    - Use Task.WhenAll to fetch AutomationId, Name, IsEnabled in parallel
    - Reduces per-element overhead
  - [ ] 15.4. Re-run benchmarks, verify performance improvement
    - Target: 50% latency reduction for large trees

- [ ] 16. Add telemetry and logging
  - Track tool usage and error rates
  - Implements non-functional requirement: 3.3 (Monitoring)
  - [ ] 16.1. Write unit tests for TelemetryCollector
    - Test tool call counting (get_ui_tree: 45 calls)
    - Test error rate tracking (click_element: 5% failure rate)
    - Test performance percentiles (p50, p95, p99)
  - [ ] 16.2. Implement structured logging
    - Use Serilog with structured log format
    - Log all tool calls with parameters (sanitized)
    - Log all errors with stack traces
    - Log performance metrics (latency, token counts)
  - [ ] 16.3. Add telemetry export
    - Write telemetry to JSON file on shutdown
    - Include session summary (total tools called, error rate, avg latency)
  - [ ] 16.4. Document telemetry in README
    - Explain what data is collected
    - Provide opt-out mechanism (TELEMETRY_DISABLED env var)

---

## Phase 7: Documentation and Examples

**Prerequisites**: Phase 6 complete

- [ ] 17. Write comprehensive MCP tool documentation
  - Document all tools with examples
  - Implements requirement: 5.3 (Documentation)
  - [ ] 17.1. Create MCP_TOOLS.md reference
    - For each tool, document: name, description, parameters, return value, example
    - Include error codes and recovery suggestions
  - [ ] 17.2. Create AGENT_GUIDE.md usage guide
    - Document OODA loop workflow
    - Document progressive disclosure pattern
    - Document anchor-based navigation pattern
    - Document self-healing recovery
  - [ ] 17.3. Create SANDBOX_SETUP.md guide
    - Document .wsb configuration format
    - Document security considerations (path validation)
    - Document troubleshooting (VM doesn't start, stdio issues)

- [ ] 18. Create example agent scripts
  - Demonstrate common automation patterns
  - Implements requirement: 5.3 (Documentation)
  - [ ] 18.1. Create example: simple form filling agent
    - Script that finds form, fills fields, clicks submit
    - Demonstrates get_ui_tree, type_text, click_element
  - [ ] 18.2. Create example: test case generation agent
    - Script that explores UI and generates test cases
    - Demonstrates progressive disclosure, state validation
  - [ ] 18.3. Create example: bug detection agent
    - Script that looks for common UI bugs (unlabeled buttons, etc.)
    - Demonstrates tree analysis, heuristic checks
  - [ ] 18.4. Add examples to repository in /examples directory

---

## Phase 8: Testing and Validation

**Prerequisites**: All phases complete

- [ ] 19. Write comprehensive E2E test suite
  - Test all tools in realistic scenarios
  - Implements requirement: 3.2 (Reliability)
  - [ ] 19.1. Create E2E test: Full exploration workflow
    - Launch TestApp in sandbox
    - Agent explores UI tree progressively
    - Agent identifies all interactive elements
    - Agent tests each button/textbox
    - Verify no exceptions, all actions succeed
  - [ ] 19.2. Create E2E test: Security validation
    - Launch TestApp in sandbox
    - Agent attempts to access C:\Users\jhedin\Documents (should fail)
    - Agent opens File Explorer (should only see sandbox C:\)
    - Verify host filesystem inaccessible
  - [ ] 19.3. Create E2E test: Error recovery
    - Launch TestApp, agent caches elements
    - Kill and restart TestApp
    - Agent continues automation (triggers self-healing)
    - Verify agent recovers and completes task
  - [ ] 19.4. Create E2E test: Performance under load
    - Launch TestApp with 1000+ UI elements
    - Agent calls get_ui_tree repeatedly
    - Verify latency stays <2s per call
    - Verify memory usage stable (no leaks)

- [ ] 20. Perform security audit
  - Validate sandbox isolation effectiveness
  - Implements requirement: 2.7 (Sandboxing & Safety)
  - [ ] 20.1. Manual security test: File deletion prevention
    - Launch TestApp in sandbox with File menu
    - Agent clicks File → Open → navigates to C:\Users\jhedin\Documents
    - Verify path does NOT exist in sandbox
    - Verify agent cannot delete host files
  - [ ] 20.2. Manual security test: Network isolation
    - Launch TestApp in sandbox with network-disabled .wsb
    - Agent attempts web request (if app has browser control)
    - Verify request fails (no network access)
  - [ ] 20.3. Manual security test: .wsb validation bypass attempts
    - Attempt to pass .wsb with sensitive path mapping
    - Verify launch_app_sandboxed rejects configuration
    - Attempt path traversal tricks (../, symlinks)
    - Verify validation catches all bypass attempts
  - [ ] 20.4. Document security findings
    - Create SECURITY_AUDIT.md with test results
    - List any residual risks (if any)
    - Provide security best practices for users

---

## Migration Tasks

These tasks update existing MCP server code to integrate new features.

- [ ] 21. Update existing tools for PID filtering
  - Modify all tools to respect session active_pid
  - Implements requirement: 2.6.1
  - [ ] 21.1. Modify find_element to filter by PID
    - Add PID check before returning elements
    - If active_pid set, skip windows from other processes
  - [ ] 21.2. Modify click_element to validate PID
    - Before clicking, verify element belongs to active_pid
    - Return error if element is from different process
  - [ ] 21.3. Modify type_text to validate PID
  - [ ] 21.4. Update all existing tools similarly

- [ ] 22. Refactor session management
  - Centralize session state in SessionManager class
  - Implements design section 3.1
  - [ ] 22.1. Create SessionManager class
    - Properties: active_pid, element_cache, event_queue, sandbox_handles
    - Methods: CacheElement(), GetCachedElement(), ClearSession()
  - [ ] 22.2. Modify Program.cs to use SessionManager
    - Replace global dictionaries with SessionManager instance
    - Pass SessionManager to all tool handlers
  - [ ] 22.3. Add session lifecycle management
    - Initialize session on first tool call
    - Clear session on close_sandbox or explicit reset
  - [ ] 22.4. Write unit tests for SessionManager
    - Test element caching (add, retrieve, expiration)
    - Test PID tracking
    - Test session reset clears all state

---

## Success Criteria

Implementation is complete when:

1. All 22 task groups above are checked off
2. All unit tests pass (dotnet test)
3. All integration tests pass
4. All E2E tests pass (including security tests)
5. Documentation complete (MCP_TOOLS.md, AGENT_GUIDE.md, SANDBOX_SETUP.md)
6. Security audit passed with no critical findings
7. Performance benchmarks met (<500ms typical, <2s complex trees)
8. Example agent scripts run successfully

## Out of Scope

The following are explicitly NOT included in this implementation:

- AI model integration (agent logic lives outside MCP server)
- Web browser automation (only Windows desktop apps)
- Cross-platform support (Windows-only)
- Visual validation (screenshots, OCR) - already exists via take_screenshot
- Mobile device automation
- Legacy Windows app support (Win32 HWND-based, not UIA-compatible)
