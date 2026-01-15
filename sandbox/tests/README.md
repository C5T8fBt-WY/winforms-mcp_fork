# Sandbox Integration Tests

This directory contains integration tests for the Windows Sandbox hot-reload workflow.

## Prerequisites

- Windows 10/11 Pro or Enterprise with Windows Sandbox feature enabled
- PowerShell 5.1+ (comes with Windows)
- Run setup.ps1 once before testing

## Test Categories

| Category | Script | Description |
|----------|--------|-------------|
| **Smoke** | `Test-Smoke.ps1` | Basic functionality - sandbox launches, ready signal |
| **Hot-Reload** | `Test-HotReload.ps1` | Code change triggers reload |
| **Debounce** | `Test-Debounce.ps1` | Rapid changes coalesce into single build |
| **Concurrency** | `Test-Concurrency.ps1` | Build lock prevents overlapping builds |
| **FileSystem** | `Test-FileSystem.ps1` | FSW vs polling, atomic writes |
| **JobObject** | `Test-JobObject.ps1` | Process cleanup on exit |
| **Crash Recovery** | `Test-CrashRecovery.ps1` | Handles process crashes gracefully |
| **Edge Cases** | `Test-EdgeCases.ps1` | Burst updates, large files, etc. |

## Running Tests

```powershell
# Run all tests
.\Run-AllTests.ps1

# Run specific category
.\Run-AllTests.ps1 -Category Smoke

# Run with verbose output
.\Run-AllTests.ps1 -Verbose

# Run without cleanup (keep sandbox running for inspection)
.\Run-AllTests.ps1 -NoCleanup
```

## Test Output

Tests write results to:
- `test-results.json` - Machine-readable results
- `test-results.log` - Human-readable log
- `C:\WinFormsMcpSandboxWorkspace\Shared\bootstrap.log` - Sandbox-side logs

## CI/CD Notes

These tests require Windows Sandbox and cannot run in typical CI environments.
Options for automated testing:

1. **Self-hosted Windows runner** with Sandbox enabled
2. **Azure VM** with nested virtualization
3. **Manual gate** - run before release
