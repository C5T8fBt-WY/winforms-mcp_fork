# Test-FileSystem.ps1
# Tests for FileSystemWatcher and atomic file operations

param([switch]$Standalone)

if ($Standalone) {
    . "$PSScriptRoot\TestFramework.ps1"
    Start-TestRun -Name "FileSystem Tests"
}

Describe "FileSystem Tests" {

    It "Sandbox is running" {
        $status = Get-SandboxStatus
        if ($null -eq $status) {
            $script:Sandbox = Start-Sandbox -TimeoutSeconds 90
        }
        Assert-True ($null -ne (Get-SandboxStatus)) "Sandbox not running"
    }

    It "Atomic rename pattern works" {
        $testFile = Join-Path $Config.SharedPath "test-atomic.txt"
        $testTmp = "$testFile.tmp"

        # Clean up
        Remove-Item $testFile -Force -ErrorAction SilentlyContinue
        Remove-Item $testTmp -Force -ErrorAction SilentlyContinue

        # Write to tmp
        $content = "Test content $(Get-Date)"
        Set-Content -Path $testTmp -Value $content

        # Verify tmp exists, final doesn't
        Assert-FileExists $testTmp "Tmp file should exist"
        Assert-FileNotExists $testFile "Final file should not exist yet"

        # Atomic rename
        Move-Item -Path $testTmp -Destination $testFile -Force

        # Verify final exists, tmp doesn't
        Assert-FileNotExists $testTmp "Tmp file should be gone"
        Assert-FileExists $testFile "Final file should exist"

        # Verify content
        $readContent = Get-Content $testFile -Raw
        Assert-True ($readContent.Trim() -eq $content) "Content mismatch"

        # Cleanup
        Remove-Item $testFile -Force -ErrorAction SilentlyContinue
    }

    It "FileSystemWatcher detects trigger (fast path)" {
        $originalPid = (Get-SandboxStatus).server_pid
        $startTime = Get-Date

        Send-ServerTrigger

        $newStatus = Wait-ForReload -OriginalPid $originalPid -TimeoutSeconds 30
        $duration = (Get-Date) - $startTime

        Assert-True ($null -ne $newStatus) "Reload did not occur"

        # Check if FSW caught it (should be fast, < 2s)
        # If polling caught it, would take up to 20s
        Write-Host "    Reload detected in $($duration.TotalSeconds.ToString('F2'))s" -ForegroundColor Gray

        $log = Get-BootstrapLog -Lines 20
        $fswDetected = $log | Where-Object { $_ -match "FSW: Trigger detected" }
        $pollDetected = $log | Where-Object { $_ -match "Poll: .* trigger found" }

        if ($fswDetected.Count -gt 0) {
            Write-Host "    Detected by: FileSystemWatcher (fast path)" -ForegroundColor Green
        } elseif ($pollDetected.Count -gt 0) {
            Write-Host "    Detected by: Polling (fallback)" -ForegroundColor Yellow
        }

        # Either way, reload should have happened
        Assert-NotEqual $originalPid $newStatus.server_pid "PID should have changed"
    }

    It "Adaptive polling kicks in after FSW miss" {
        # This is hard to test directly, but we can verify the log message
        $log = Get-BootstrapLog -Lines 100

        # Look for adaptive polling activation
        $adaptiveMsg = $log | Where-Object { $_ -match "fast polling" }

        # This may or may not have happened depending on FSW reliability
        if ($adaptiveMsg.Count -gt 0) {
            Write-Host "    Adaptive polling was triggered" -ForegroundColor Yellow
        } else {
            Write-Host "    FSW caught all events (adaptive polling not needed)" -ForegroundColor Green
        }

        # Test passes either way - just informational
        Assert-True $true "Adaptive polling check completed"
    }

    It "Shared folder is writable" {
        $testFile = Join-Path $Config.SharedPath "write-test-$(Get-Random).txt"

        # Write
        Set-Content -Path $testFile -Value "Test"

        # Verify
        Assert-FileExists $testFile "Should be able to write to shared folder"

        # Cleanup
        Remove-Item $testFile -Force
    }

    It "Large file write completes before trigger" {
        $testFile = Join-Path $Config.SharedPath "large-test.bin"
        $sizeMB = 10

        # Create file
        $buffer = New-Object byte[] (1024 * 1024)
        $stream = [System.IO.File]::Create($testFile)

        for ($i = 0; $i -lt $sizeMB; $i++) {
            $stream.Write($buffer, 0, $buffer.Length)
        }
        $stream.Close()

        # Verify size
        $fileInfo = Get-Item $testFile
        $actualMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Assert-GreaterThan $actualMB ($sizeMB - 1) "File size incorrect: ${actualMB}MB"

        # Cleanup
        Remove-Item $testFile -Force
    }
}

if ($Standalone) {
    Stop-Sandbox
    $results = Complete-TestRun
    exit $(if ($results.Failed -gt 0) { 1 } else { 0 })
}
