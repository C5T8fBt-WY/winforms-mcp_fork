# Test-JobObject.ps1
# Tests for Windows Job Object process management

param([switch]$Standalone)

if ($Standalone) {
    . "$PSScriptRoot\TestFramework.ps1"
    Start-TestRun -Name "Job Object Tests"
}

Describe "Job Object Tests" {

    It "Job Object is created on startup" {
        # Start fresh sandbox
        Stop-Sandbox
        Start-Sleep -Seconds 2

        $sandbox = Start-Sandbox -TimeoutSeconds 90
        Assert-True ($null -ne $sandbox) "Sandbox failed to start"

        $log = Get-BootstrapLog -Lines 50
        $jobObjectMsg = $log | Where-Object { $_ -match "Job Object created" }
        Assert-GreaterThan $jobObjectMsg.Count 0 "Job Object creation not logged"
    }

    It "Server process is added to Job Object" {
        $log = Get-BootstrapLog -Lines 50
        $addedMsg = $log | Where-Object { $_ -match "added to Job Object" }
        Assert-GreaterThan $addedMsg.Count 0 "Process not added to Job Object"
    }

    It "Processes are cleaned up on shutdown" {
        $status = Get-SandboxStatus
        $serverPid = $status.server_pid

        # Shutdown sandbox
        Stop-Sandbox
        Start-Sleep -Seconds 5

        # Verify cleanup was logged
        $log = Get-BootstrapLog -Lines 50
        $cleanupMsg = $log | Where-Object { $_ -match "Job Object disposed|Cleaning up" }
        Assert-GreaterThan $cleanupMsg.Count 0 "Cleanup not logged"
    }

    It "New processes are added to Job Object after reload" {
        # Start fresh sandbox
        $sandbox = Start-Sandbox -TimeoutSeconds 90
        $originalPid = (Get-SandboxStatus).server_pid

        # Trigger reload
        Send-ServerTrigger
        $newStatus = Wait-ForReload -OriginalPid $originalPid -TimeoutSeconds 30

        Assert-True ($null -ne $newStatus) "Reload failed"

        # Check that new process was added to Job Object
        $log = Get-BootstrapLog -Lines 50
        $addedAfterReload = $log | Where-Object {
            $_ -match "Server process \(PID: $($newStatus.server_pid)\) added to Job Object"
        }
        Assert-GreaterThan $addedAfterReload.Count 0 "New process not added to Job Object"
    }
}

if ($Standalone) {
    Stop-Sandbox
    $results = Complete-TestRun
    exit $(if ($results.Failed -gt 0) { 1 } else { 0 })
}
