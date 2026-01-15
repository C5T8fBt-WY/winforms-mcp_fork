# Test-HotReload.ps1
# Tests for hot-reload functionality

param([switch]$Standalone)

if ($Standalone) {
    . "$PSScriptRoot\TestFramework.ps1"
    Start-TestRun -Name "Hot-Reload Tests"
}

Describe "Hot-Reload Tests" {

    It "Sandbox is running" {
        $status = Get-SandboxStatus
        if ($null -eq $status) {
            $script:Sandbox = Start-Sandbox -TimeoutSeconds 90
        } else {
            $script:Sandbox = @{ ServerPid = $status.server_pid }
        }
        Assert-True ($script:Sandbox.ServerPid -gt 0) "Sandbox not running"
    }

    It "Server trigger causes reload" {
        $originalPid = (Get-SandboxStatus).server_pid

        Send-ServerTrigger

        $newStatus = Wait-ForReload -OriginalPid $originalPid -TimeoutSeconds 30
        Assert-True ($null -ne $newStatus) "Server did not reload within timeout"
        Assert-NotEqual $originalPid $newStatus.server_pid "PID should change after reload"
    }

    It "Reload completes within 10 seconds" {
        $originalPid = (Get-SandboxStatus).server_pid
        $startTime = Get-Date

        Send-ServerTrigger

        $newStatus = Wait-ForReload -OriginalPid $originalPid -TimeoutSeconds 30
        $duration = (Get-Date) - $startTime

        Assert-True ($null -ne $newStatus) "Server did not reload"
        Assert-LessThan $duration.TotalSeconds 10 "Reload took too long: $($duration.TotalSeconds)s"
    }

    It "Multiple reloads work consecutively" {
        $pids = @()

        for ($i = 1; $i -le 3; $i++) {
            $currentPid = (Get-SandboxStatus).server_pid
            $pids += $currentPid

            Send-ServerTrigger
            Start-Sleep -Seconds 1

            $newStatus = Wait-ForReload -OriginalPid $currentPid -TimeoutSeconds 30
            Assert-True ($null -ne $newStatus) "Reload $i failed"
        }

        # All PIDs should be different
        $uniquePids = $pids | Select-Object -Unique
        Assert-Equal $pids.Count $uniquePids.Count "PIDs should all be different"
    }

    It "Ready signal is updated after reload" {
        $beforeTimestamp = (Get-SandboxStatus).timestamp
        $originalPid = (Get-SandboxStatus).server_pid

        Send-ServerTrigger
        $newStatus = Wait-ForReload -OriginalPid $originalPid -TimeoutSeconds 30

        Assert-True ($newStatus.timestamp -ne $beforeTimestamp) "Timestamp should be updated"
    }

    It "Bootstrap log shows reload events" {
        $log = Get-BootstrapLog -Lines 50
        $reloadMessages = $log | Where-Object { $_ -match "Server restarted|Handling trigger" }
        Assert-GreaterThan $reloadMessages.Count 0 "No reload events in log"
    }
}

if ($Standalone) {
    Stop-Sandbox
    $results = Complete-TestRun
    exit $(if ($results.Failed -gt 0) { 1 } else { 0 })
}
