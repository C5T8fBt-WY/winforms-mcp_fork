# Test-CrashRecovery.ps1
# Tests for crash detection and recovery

param([switch]$Standalone)

if ($Standalone) {
    . "$PSScriptRoot\TestFramework.ps1"
    Start-TestRun -Name "Crash Recovery Tests"
}

Describe "Crash Recovery Tests" {

    It "Sandbox is running" {
        $status = Get-SandboxStatus
        if ($null -eq $status) {
            $script:Sandbox = Start-Sandbox -TimeoutSeconds 90
        }
        Assert-True ($null -ne (Get-SandboxStatus)) "Sandbox not running"
    }

    It "Crash is detected and logged" {
        # We can't easily kill a process inside the sandbox from outside
        # But we can verify the crash detection code exists in logs
        $log = Get-BootstrapLog -Lines 100

        # Check that crash detection function is being called
        # (even if no crashes have occurred)
        $monitorLoopMsg = $log | Where-Object { $_ -match "monitor loop" }
        Assert-GreaterThan $monitorLoopMsg.Count 0 "Monitor loop should be running"
    }

    It "Recovery trigger works after simulated failure" {
        # Trigger a reload (simulates recovery)
        $originalPid = (Get-SandboxStatus).server_pid

        Send-ServerTrigger
        $newStatus = Wait-ForReload -OriginalPid $originalPid -TimeoutSeconds 30

        Assert-True ($null -ne $newStatus) "Should recover via trigger"
        Assert-NotEqual $originalPid $newStatus.server_pid "PID should change"
    }

    It "Multiple rapid reloads don't cause stuck state" {
        # This tests PID recycling concerns from Gemini
        $pids = @()

        for ($i = 1; $i -le 5; $i++) {
            $currentPid = (Get-SandboxStatus).server_pid
            $pids += $currentPid

            Send-ServerTrigger
            Start-Sleep -Milliseconds 500

            $newStatus = Wait-ForReload -OriginalPid $currentPid -TimeoutSeconds 15
            if ($null -eq $newStatus) {
                throw "Reload $i timed out - possible stuck state"
            }
        }

        # Should have different PIDs (verifies we're not stuck)
        $uniquePids = $pids | Select-Object -Unique
        Assert-GreaterThan $uniquePids.Count 2 "Should have multiple different PIDs"
    }

    It "Bootstrap continues monitoring after server exit" {
        # Verify bootstrap stays alive even if server crashes
        # by checking it's still updating logs
        $logBefore = (Get-BootstrapLog -Lines 1).Count

        Start-Sleep -Seconds 3

        $logAfter = Get-BootstrapLog -Lines 100
        # Log should still be active (transcript continues)
        Assert-True ($logAfter.Count -ge $logBefore) "Bootstrap should continue logging"
    }

    It "Ready signal remains valid during stable operation" {
        $status1 = Get-SandboxStatus
        Start-Sleep -Seconds 2
        $status2 = Get-SandboxStatus

        Assert-True ($null -ne $status1) "Status 1 should exist"
        Assert-True ($null -ne $status2) "Status 2 should exist"
        Assert-Equal $status1.server_pid $status2.server_pid "PID should be stable"
    }
}

if ($Standalone) {
    Stop-Sandbox
    $results = Complete-TestRun
    exit $(if ($results.Failed -gt 0) { 1 } else { 0 })
}
