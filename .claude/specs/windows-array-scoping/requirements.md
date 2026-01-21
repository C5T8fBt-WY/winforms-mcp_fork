# Requirements: Windows Array Scoping

## Problem Statement

Every MCP tool response currently includes ALL visible windows on the desktop (200-800 tokens per response). This is wasteful because:
1. Agents rarely need information about windows unrelated to their current task
2. Token overhead adds up across many tool calls in a workflow
3. Irrelevant windows can confuse agents when deciding next actions

The windows array IS useful - it helps agents avoid extra calls to get window info after actions. But it should be scoped to relevant windows, not the entire desktop.

## User Stories (EARS Format)

### US-1: App-Scoped Windows for Launch
**When** an agent launches an application via `launch_app`,
**the system shall** return only windows belonging to the launched process and its children in the `windows` array,
**so that** agents see the app's windows immediately without irrelevant desktop clutter.

**Acceptance Criteria:**
- AC-1.1: Response includes main window of launched process
- AC-1.2: Response includes any child/dialog windows of the process
- AC-1.3: Response excludes windows from other processes
- AC-1.4: Token count for windows array is reduced by 70%+ in typical scenarios

### US-2: App-Scoped Windows for Element Operations
**When** an agent performs element operations (`click_element`, `type_text`, `find_element`, etc.) that target a specific window,
**the system shall** return only windows belonging to the same process as the targeted element,
**so that** agents maintain focus on the app they're automating.

**Acceptance Criteria:**
- AC-2.1: Operations with `windowTitle` or `windowHandle` parameters scope to that window's process
- AC-2.2: Operations using cached element IDs scope to the element's containing window's process
- AC-2.3: Child windows (dialogs, popups) from the same process are included

### US-3: Desktop-Wide Windows for Discovery
**When** an agent explicitly requests full window discovery via specific tools (`list_sandbox_apps`, `get_capabilities`, or a new `list_windows` tool),
**the system shall** return all visible windows,
**so that** agents can discover available applications for multi-app workflows.

**Acceptance Criteria:**
- AC-3.1: `list_sandbox_apps` returns all windows (maintains current behavior)
- AC-3.2: `get_capabilities` returns all windows (maintains current behavior)
- AC-3.3: A mechanism exists for agents to explicitly request all windows when needed

### US-4: Error Recovery with Expanded Context
**While** a tool operation fails with a window-related error (window not found, element not found),
**the system shall** include all windows in the response,
**so that** agents have full context to diagnose and recover from the error.

**Acceptance Criteria:**
- AC-4.1: Window-not-found errors include full window list
- AC-4.2: Element-not-found errors include full window list
- AC-4.3: Multiple-match errors include full window list plus the matches
- AC-4.4: `partialMatches` property continues to work as before

### US-5: Multi-App Workflow Support
**When** an agent is automating multiple applications simultaneously,
**the system shall** track and scope windows to all actively-tracked processes,
**so that** agents can work across apps without losing context on any of them.

**Acceptance Criteria:**
- AC-5.1: `attach_to_process` adds a process to the active tracking set
- AC-5.2: Multiple launched apps remain in the tracking set
- AC-5.3: Windows from all tracked processes appear in responses
- AC-5.4: `close_app` removes a process from the tracking set

### US-6: Opt-out Mechanism
**When** an agent needs all windows regardless of current context,
**the system shall** provide a parameter to disable scoping for that request,
**so that** agents retain full control over window visibility when needed.

**Acceptance Criteria:**
- AC-6.1: Tools accept an optional `includeAllWindows` parameter
- AC-6.2: When `includeAllWindows=true`, response includes all desktop windows
- AC-6.3: Default behavior (no parameter) uses smart scoping

## Non-Functional Requirements

### NFR-1: Performance
- Window enumeration must not add more than 10ms latency
- Process filtering must not add more than 5ms per response

### NFR-2: Backwards Compatibility
- Existing agent workflows must not break
- The `windows` array remains in the same location in responses
- Window object schema remains unchanged

### NFR-3: Token Reduction
- Average token reduction of 60%+ for single-app workflows
- Worst case: no regression (all windows returned on error/discovery)

## Out of Scope

- Changing the WindowInfo schema
- Modifying how window handles are formatted
- Cross-session window tracking
- Window caching/memoization between calls
