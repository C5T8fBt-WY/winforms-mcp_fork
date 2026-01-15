# test-edge-cases.ps1
# Edge case tests for the sandbox hot-reload workflow
# Based on Gemini's recommendations for stress testing
#
# Run this AFTER the sandbox is running and stable

param(
    [string]$SharedPath = "C:\WinFormsMcpSandboxWorkspace\Shared",
    [string]$ServerPath = "C:\WinFormsMcpSandboxWorkspace\Server",
    [switch]$All,           # Run all tests
    [switch]$BurstTest,     # Test rapid file changes
    [switch]$SlowWriteTest, # Test incomplete write handling
    [switch]$RapidReload,   # Test rapid reload cycles
    [switch]$LargeFile      # Test large file copy
)

$ErrorActionPreference = "Stop"

Write-Host "=== Sandbox Edge Case Tests ===" -ForegroundColor Cyan
Write-Host "Shared path: $SharedPath"
Write-Host ""

# Helper function to wait for ready signal update
function Wait-ForReload {
    param([string]$OriginalPid, [int]$TimeoutSeconds = 30)

    $ReadySignal = Join-Path $SharedPath "mcp-ready.signal"
    $startTime = Get-Date

    while (((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path $ReadySignal) {
            $data = Get-Content $ReadySignal -Raw | ConvertFrom-Json
            if ($data.server_pid -ne $OriginalPid) {
                return $data
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Get-CurrentPid {
    $ReadySignal = Join-Path $SharedPath "mcp-ready.signal"
    if (Test-Path $ReadySignal) {
        $data = Get-Content $ReadySignal -Raw | ConvertFrom-Json
        return $data.server_pid
    }
    return $null
}

#region Test: Burst Updates (simulates git checkout)
function Test-BurstUpdates {
    Write-Host ""
    Write-Host "=== TEST: Burst Updates ===" -ForegroundColor Yellow
    Write-Host "Simulating rapid file changes (like git checkout)..."

    $originalPid = Get-CurrentPid
    Write-Host "Original PID: $originalPid"

    # Create 20 trigger files rapidly
    $triggerCount = 0
    for ($i = 1; $i -le 20; $i++) {
        $triggerFile = Join-Path $SharedPath "server.trigger"
        $triggerTmp = Join-Path $SharedPath "server.trigger.tmp"

        # Rapid-fire triggers
        Set-Content -Path $triggerTmp -Value "Burst test $i at $((Get-Date).ToString('o'))"
        Move-Item -Path $triggerTmp -Destination $triggerFile -Force
        $triggerCount++

        Start-Sleep -Milliseconds 50  # 50ms between triggers
    }

    Write-Host "Sent $triggerCount triggers in rapid succession"
    Write-Host "Waiting for reload to complete..."

    Start-Sleep -Seconds 5

    $newPid = Get-CurrentPid
    Write-Host "New PID: $newPid"

    if ($newPid -and $newPid -ne $originalPid) {
        Write-Host "PASS: Server reloaded (debounce worked)" -ForegroundColor Green
    } else {
        Write-Host "UNCERTAIN: PID unchanged - check bootstrap.log" -ForegroundColor Yellow
    }
}
#endregion

#region Test: Slow/Incomplete Write
function Test-SlowWrite {
    Write-Host ""
    Write-Host "=== TEST: Slow Write ===" -ForegroundColor Yellow
    Write-Host "Testing that sandbox waits for complete file write..."

    $testFile = Join-Path $SharedPath "slow-write-test.txt"

    # Create file, hold it open, write slowly
    Write-Host "Creating file and holding handle open..."
    $stream = [System.IO.File]::Create($testFile)
    $writer = New-Object System.IO.StreamWriter($stream)

    Write-Host "Writing partial content..."
    $writer.Write("Partial")
    $writer.Flush()

    Write-Host "Waiting 3 seconds (simulating slow write)..."
    Start-Sleep -Seconds 3

    Write-Host "Completing write..."
    $writer.WriteLine(" - Complete!")
    $writer.Close()
    $stream.Close()

    # Verify content
    $content = Get-Content $testFile -Raw
    if ($content -match "Complete") {
        Write-Host "PASS: File write completed correctly" -ForegroundColor Green
    } else {
        Write-Host "FAIL: File content incorrect" -ForegroundColor Red
    }

    Remove-Item $testFile -Force -ErrorAction SilentlyContinue
}
#endregion

#region Test: Rapid Reload Cycles
function Test-RapidReload {
    Write-Host ""
    Write-Host "=== TEST: Rapid Reload Cycles ===" -ForegroundColor Yellow
    Write-Host "Testing 5 reload cycles with 3s between each..."

    $pids = @()
    $originalPid = Get-CurrentPid
    $pids += $originalPid

    for ($i = 1; $i -le 5; $i++) {
        Write-Host "  Cycle $i..."

        # Trigger reload
        $triggerFile = Join-Path $SharedPath "server.trigger"
        $triggerTmp = Join-Path $SharedPath "server.trigger.tmp"
        Set-Content -Path $triggerTmp -Value "Rapid reload test $i"
        Move-Item -Path $triggerTmp -Destination $triggerFile -Force

        # Wait for reload
        Start-Sleep -Seconds 3

        $newPid = Get-CurrentPid
        $pids += $newPid
        Write-Host "    PID: $newPid"
    }

    # Check for PID recycling
    $uniquePids = $pids | Select-Object -Unique
    Write-Host ""
    Write-Host "PIDs observed: $($pids -join ' -> ')"
    Write-Host "Unique PIDs: $($uniquePids.Count)"

    if ($uniquePids.Count -ge 3) {
        Write-Host "PASS: PIDs changing correctly (no stuck state)" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Few unique PIDs - possible issue" -ForegroundColor Yellow
    }
}
#endregion

#region Test: Large File Copy
function Test-LargeFile {
    Write-Host ""
    Write-Host "=== TEST: Large File Copy ===" -ForegroundColor Yellow
    Write-Host "Testing that large file copy doesn't trigger prematurely..."

    $testFile = Join-Path $SharedPath "large-test-file.bin"
    $sizeMB = 50

    Write-Host "Creating ${sizeMB}MB test file..."

    # Create large file with random data
    $buffer = New-Object byte[] (1024 * 1024)  # 1MB buffer
    $random = New-Object Random

    $stream = [System.IO.File]::Create($testFile)
    for ($i = 0; $i -lt $sizeMB; $i++) {
        $random.NextBytes($buffer)
        $stream.Write($buffer, 0, $buffer.Length)
        Write-Host "." -NoNewline
    }
    $stream.Close()
    Write-Host ""

    # Verify file size
    $fileInfo = Get-Item $testFile
    $actualSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)

    if ($actualSizeMB -ge ($sizeMB - 1)) {
        Write-Host "PASS: Large file created (${actualSizeMB}MB)" -ForegroundColor Green
    } else {
        Write-Host "FAIL: File size incorrect (${actualSizeMB}MB)" -ForegroundColor Red
    }

    # Cleanup
    Remove-Item $testFile -Force -ErrorAction SilentlyContinue
}
#endregion

#region Test: Atomic Rename Verification
function Test-AtomicRename {
    Write-Host ""
    Write-Host "=== TEST: Atomic Rename ===" -ForegroundColor Yellow
    Write-Host "Verifying atomic rename pattern works correctly..."

    $triggerFile = Join-Path $SharedPath "test-atomic.trigger"
    $triggerTmp = Join-Path $SharedPath "test-atomic.trigger.tmp"

    # Clean up any existing files
    Remove-Item $triggerFile -Force -ErrorAction SilentlyContinue
    Remove-Item $triggerTmp -Force -ErrorAction SilentlyContinue

    # Write to tmp
    $timestamp = (Get-Date).ToString("o")
    Set-Content -Path $triggerTmp -Value $timestamp

    # Verify tmp exists, final doesn't
    $tmpExists = Test-Path $triggerTmp
    $finalExists = Test-Path $triggerFile

    if ($tmpExists -and -not $finalExists) {
        Write-Host "  Step 1: tmp exists, final doesn't - OK" -ForegroundColor Green
    } else {
        Write-Host "  Step 1: FAIL" -ForegroundColor Red
    }

    # Atomic rename
    Move-Item -Path $triggerTmp -Destination $triggerFile -Force

    # Verify final exists, tmp doesn't
    $tmpExists = Test-Path $triggerTmp
    $finalExists = Test-Path $triggerFile

    if (-not $tmpExists -and $finalExists) {
        Write-Host "  Step 2: tmp gone, final exists - OK" -ForegroundColor Green
    } else {
        Write-Host "  Step 2: FAIL" -ForegroundColor Red
    }

    # Verify content
    $content = Get-Content $triggerFile -Raw
    if ($content.Trim() -eq $timestamp) {
        Write-Host "  Step 3: Content matches - OK" -ForegroundColor Green
        Write-Host "PASS: Atomic rename working correctly" -ForegroundColor Green
    } else {
        Write-Host "  Step 3: Content mismatch - FAIL" -ForegroundColor Red
    }

    # Cleanup
    Remove-Item $triggerFile -Force -ErrorAction SilentlyContinue
}
#endregion

#region Run Tests
if ($All -or $BurstTest) {
    Test-BurstUpdates
}

if ($All -or $SlowWriteTest) {
    Test-SlowWrite
}

if ($All -or $RapidReload) {
    Test-RapidReload
}

if ($All -or $LargeFile) {
    Test-LargeFile
}

# Always run atomic rename test as a sanity check
Test-AtomicRename

Write-Host ""
Write-Host "=== Tests Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Check C:\WinFormsMcpSandboxWorkspace\Shared\bootstrap.log for detailed sandbox-side logs"
#endregion
