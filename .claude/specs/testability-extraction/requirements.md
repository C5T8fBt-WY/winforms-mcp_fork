# Requirements: Testability Extraction

## Overview

Extract pure, unit-testable logic from Windows-dependent code to maximize test coverage while keeping integration tests thin.

## Problem Statement

The WinForms MCP server contains significant business logic mixed with Windows-only APIs (FlaUI, Win32 P/Invoke, UI Automation). This makes the codebase difficult to test:
- Unit tests require Windows environment
- Integration tests are slow and flaky
- Code coverage is low despite working functionality
- 3 unit tests are currently failing (ScriptRunner type preservation)

## Functional Requirements

### FR-1: Pure Utility Extraction

**FR-1.1** The system SHALL extract argument parsing helpers into a unit-testable static class.

**FR-1.2** The system SHALL extract variable interpolation logic into a unit-testable class that:
- Preserves JSON types (numbers, booleans, strings) during interpolation
- Operates on JSON DOM, not string replacement
- Provides clear error messages for resolution failures

**FR-1.3** The system SHALL extract coordinate math into a unit-testable static class covering:
- Pixel to HIMETRIC conversion
- DPI scaling calculations
- Window-to-screen coordinate translation

**FR-1.4** The system SHALL extract token estimation into a unit-testable static class.

### FR-2: Interface-Based Abstractions

**FR-2.1** The system SHALL define interfaces for Windows-dependent operations:
- `IProcessChecker` for process liveness checks
- `IWindowProvider` for window enumeration
- `IElementProvider` for UI element operations

**FR-2.2** Service classes SHALL accept interfaces via constructor for testability.

**FR-2.3** Time-dependent services SHALL accept injectable clock (`Func<DateTime>`) for testing.

### FR-3: Model Extraction

**FR-3.1** The system SHALL define pure data models separate from FlaUI types:
- `ElementInfo` for element properties without AutomationElement dependency
- `FindCriteria` for element search parameters
- `TreeNode` for tree structure without FlaUI traversal

### FR-4: Test Coverage

**FR-4.1** Extracted utilities SHALL have 100% branch coverage via unit tests.

**FR-4.2** Interface-based services SHALL have 100% branch coverage via unit tests with mocks.

**FR-4.3** Integration tests SHALL only verify Windows API wiring, not business logic.

## Non-Functional Requirements

### NFR-1: No Breaking Changes
- Existing handler code continues to work without modification
- Public API remains unchanged
- Extracted classes are internal implementation details

### NFR-2: Performance
- Extraction SHALL NOT introduce measurable performance regression
- Avoid unnecessary object allocation in hot paths

### NFR-3: Maintainability
- Each extracted class has single responsibility
- Clear separation between pure logic and Windows dependencies

## Acceptance Criteria

1. **Variable Interpolation Tests Pass**: The 3 failing tests in ScriptExecutionTests.cs pass after extraction
2. **New Unit Tests**: At least 50 new unit tests for extracted utilities
3. **Coverage Increase**: Unit-testable code coverage increases to 40%+ from current ~30%
4. **Integration Tests Thin**: Integration tests reduced to wiring verification only

## Out of Scope

- Changing public MCP tool interfaces
- Adding new MCP tools
- Modifying FlaUI usage patterns
- Cross-platform support
