# Requirements: Program.cs Refactoring

## Overview

The current `Program.cs` is a 4,989 LOC monolith containing the entire MCP server implementation. This refactoring will extract cohesive units into separate classes to improve maintainability, testability, and separation of concerns.

## Requirements (EARS Format)

### REQ-1: Tool Handler Extraction
**UBIQUITOUS:** The system shall organize tool handlers into dedicated handler classes grouped by functional domain (process, element, input, screenshot, script).

**Acceptance Criteria:**
- Each handler class contains only tools for its domain
- Handler classes receive dependencies via constructor injection
- Tool registration remains centralized in AutomationServer
- No changes to MCP protocol or tool signatures

### REQ-2: Constants Extraction
**UBIQUITOUS:** The system shall define all magic numbers, timeout values, and protocol strings in a centralized `Constants.cs` file.

**Acceptance Criteria:**
- JSON-RPC error codes defined as named constants
- Default timeout values (e.g., 30000ms) as constants
- Protocol version strings as constants
- Server capability strings as constants
- All existing magic numbers replaced with constant references

### REQ-3: Protocol Handler Separation
**UBIQUITOUS:** The system shall encapsulate JSON-RPC 2.0 protocol handling in a dedicated `McpProtocol.cs` class.

**Acceptance Criteria:**
- Request parsing and validation isolated
- Response formatting isolated
- Error response generation centralized
- Tool definitions (GetToolDefinitions) isolated from handlers

### REQ-4: Script Runner Extraction
**UBIQUITOUS:** The system shall extract script execution logic (run_script tool) into a dedicated `ScriptRunner.cs` class.

**Acceptance Criteria:**
- Variable interpolation logic in ScriptRunner
- Step execution orchestration in ScriptRunner
- Timeout and error handling in ScriptRunner
- ScriptRunner can invoke tool handlers via interface

### REQ-5: AutomationServer Simplification
**UBIQUITOUS:** The system shall reduce AutomationServer to orchestration responsibilities only.

**Acceptance Criteria:**
- AutomationServer under 500 LOC
- Clear dependency graph
- Single responsibility: coordinate handlers and protocol
- TCP and stdio communication logic remains in Program/AutomationServer

### REQ-6: Backward Compatibility
**UBIQUITOUS:** The system shall maintain identical MCP protocol behavior after refactoring.

**Acceptance Criteria:**
- All existing tool names unchanged
- All existing tool parameters unchanged
- All existing response formats unchanged
- All existing E2E tests pass without modification

### REQ-7: Testability
**UBIQUITOUS:** The system shall enable unit testing of individual tool handlers without running the full server.

**Acceptance Criteria:**
- Handlers can be instantiated independently
- Dependencies are injectable (SessionManager, WindowManager)
- No static state prevents parallel test execution

## Out of Scope

- New tool additions
- Protocol version changes
- Performance optimizations (beyond reduced complexity)
- UI changes to TestApp

## Dependencies

- FlaUI library (unchanged)
- .NET 8.0 (unchanged)
- Existing test infrastructure
