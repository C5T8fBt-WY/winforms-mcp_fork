# Test-Debounce.ps1
# Tests for debounce and concurrency behavior

param([switch]$Standalone)

if ($Standalone) {
    . "$PSScriptRoot\TestFramework.ps1"
    Start-TestRun -Name "Debounce Tests"
}

Describe "Debounce Tests" {

    It "Sandbox is running" {
        $status = Get-SandboxStatus
        if ($null -eq $status) {
            $script:Sandbox = Start-Sandbox -TimeoutSeconds 90
        }
        Assert-True ($null -ne (Get-SandboxStatus)) "Sandbox not running"
    }

    It "Rapid triggers coalesce into fewer reloads" {
        $originalPid = (Get-SandboxStatus).server_pid

        # Send 10 triggers in rapid succession (faster than debounce)
        for ($i = 1; $i -le 10; $i++) {
            Send-ServerTrigger
            Start-Sleep -Milliseconds 30  # Faster than 100ms debounce
        }

        # Wait for any reloads to complete
        Start-Sleep -Seconds 5

        $finalPid = (Get-SandboxStatus).server_pid

        # Should have reloaded (PID changed)
        Assert-NotEqual $originalPid $finalPid "Should have reloaded at least once"

        # Check log for number of restarts
        $log = Get-BootstrapLog -Lines 100
        $restartCount = ($log | Where-Object { $_ -match "Server restarted" }).Count

        # Due to debouncing, should have far fewer restarts than triggers sent
        Write-Host "    Triggers sent: 10, Restarts: $restartCount" -ForegroundColor Gray
        Assert-LessThan $restartCount 5 "Too many restarts ($restartCount) - debounce not working"
    }

    It "Burst of 20 triggers results in minimal reloads" {
        # Clear log context by waiting
        Start-Sleep -Seconds 2
        $logBefore = (Get-BootstrapLog -Lines 1000).Count

        $originalPid = (Get-SandboxStatus).server_pid

        # Rapid burst
        for ($i = 1; $i -le 20; $i++) {
            Send-ServerTrigger
            Start-Sleep -Milliseconds 20
        }

        # Wait for processing
        Start-Sleep -Seconds 8

        # Count new restart messages
        $logAfter = Get-BootstrapLog -Lines 1000
        $newLines = $logAfter | Select-Object -Skip $logBefore
        $restartCount = ($newLines | Where-Object { $_ -match "Server restarted" }).Count

        Write-Host "    Burst triggers: 20, New restarts: $restartCount" -ForegroundColor Gray
        Assert-LessThan $restartCount 5 "Burst caused too many restarts"
    }

    It "Triggers during reload are queued" {
        $originalPid = (Get-SandboxStatus).server_pid

        # Send trigger
        Send-ServerTrigger

        # Immediately send more while reload is happening
        Start-Sleep -Milliseconds 500
        Send-ServerTrigger
        Send-ServerTrigger

        # Wait for everything to settle
        Start-Sleep -Seconds 5

        $finalPid = (Get-SandboxStatus).server_pid
        Assert-NotEqual $originalPid $finalPid "Should have completed reload"

        # Server should be stable now
        $pidCheck1 = (Get-SandboxStatus).server_pid
        Start-Sleep -Seconds 2
        $pidCheck2 = (Get-SandboxStatus).server_pid

        Assert-Equal $pidCheck1 $pidCheck2 "Server should be stable after processing queue"
    }
}

if ($Standalone) {
    Stop-Sandbox
    $results = Complete-TestRun
    exit $(if ($results.Failed -gt 0) { 1 } else { 0 })
}
