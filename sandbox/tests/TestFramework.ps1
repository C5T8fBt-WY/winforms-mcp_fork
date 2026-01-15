# TestFramework.ps1
# Minimal test framework for sandbox integration tests

$script:TestResults = @()
$script:CurrentTest = $null

# Configuration
$script:Config = @{
    TransportTestPath = "C:\TransportTest"
    SharedPath = "C:\TransportTest\Shared"
    ServerPath = "C:\TransportTest\Server"
    AppPath = "C:\TransportTest\App"
    SandboxConfig = "C:\TransportTest\sandbox-dev.wsb"
    ReadySignal = "C:\TransportTest\Shared\mcp-ready.signal"
    ShutdownSignal = "C:\TransportTest\Shared\shutdown.signal"
    ServerTrigger = "C:\TransportTest\Shared\server.trigger"
    AppTrigger = "C:\TransportTest\Shared\app.trigger"
    BootstrapLog = "C:\TransportTest\Shared\bootstrap.log"
    DefaultTimeout = 60
}

#region Test Lifecycle

function Start-TestRun {
    param([string]$Name = "Sandbox Integration Tests")

    $script:TestResults = @()
    $script:RunStartTime = Get-Date
    $script:RunName = $Name

    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host " $Name" -ForegroundColor Cyan
    Write-Host " Started: $($script:RunStartTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Gray
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host ""
}

function Complete-TestRun {
    $duration = (Get-Date) - $script:RunStartTime

    $passed = ($script:TestResults | Where-Object { $_.Status -eq "Passed" }).Count
    $failed = ($script:TestResults | Where-Object { $_.Status -eq "Failed" }).Count
    $skipped = ($script:TestResults | Where-Object { $_.Status -eq "Skipped" }).Count

    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host " Test Run Complete" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Passed:  $passed" -ForegroundColor Green
    Write-Host "  Failed:  $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Gray" })
    Write-Host "  Skipped: $skipped" -ForegroundColor Yellow
    Write-Host "  Duration: $($duration.ToString('mm\:ss\.fff'))"
    Write-Host ""

    # Return results object
    return @{
        Name = $script:RunName
        StartTime = $script:RunStartTime
        EndTime = Get-Date
        Duration = $duration
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        Tests = $script:TestResults
    }
}

function Describe {
    param(
        [string]$Name,
        [scriptblock]$Tests
    )

    Write-Host ""
    Write-Host "[$Name]" -ForegroundColor Cyan

    & $Tests
}

function It {
    param(
        [string]$Name,
        [scriptblock]$Test,
        [switch]$Skip,
        [string]$SkipReason
    )

    $script:CurrentTest = @{
        Name = $Name
        StartTime = Get-Date
        Status = "Running"
        Error = $null
    }

    Write-Host "  - $Name... " -NoNewline

    if ($Skip) {
        $script:CurrentTest.Status = "Skipped"
        $script:CurrentTest.Error = $SkipReason
        Write-Host "SKIP" -ForegroundColor Yellow
        if ($SkipReason) { Write-Host "    ($SkipReason)" -ForegroundColor Gray }
    }
    else {
        try {
            & $Test
            $script:CurrentTest.Status = "Passed"
            Write-Host "PASS" -ForegroundColor Green
        }
        catch {
            $script:CurrentTest.Status = "Failed"
            $script:CurrentTest.Error = $_.Exception.Message
            Write-Host "FAIL" -ForegroundColor Red
            Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    $script:CurrentTest.EndTime = Get-Date
    $script:CurrentTest.Duration = $script:CurrentTest.EndTime - $script:CurrentTest.StartTime
    $script:TestResults += $script:CurrentTest
}

#endregion

#region Assertions

function Assert-True {
    param([bool]$Condition, [string]$Message = "Expected true but got false")
    if (-not $Condition) { throw $Message }
}

function Assert-False {
    param([bool]$Condition, [string]$Message = "Expected false but got true")
    if ($Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        $msg = if ($Message) { $Message } else { "Expected '$Expected' but got '$Actual'" }
        throw $msg
    }
}

function Assert-NotEqual {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) {
        $msg = if ($Message) { $Message } else { "Expected value to not equal '$Expected'" }
        throw $msg
    }
}

function Assert-FileExists {
    param([string]$Path, [string]$Message)
    if (-not (Test-Path $Path)) {
        $msg = if ($Message) { $Message } else { "File not found: $Path" }
        throw $msg
    }
}

function Assert-FileNotExists {
    param([string]$Path, [string]$Message)
    if (Test-Path $Path) {
        $msg = if ($Message) { $Message } else { "File should not exist: $Path" }
        throw $msg
    }
}

function Assert-GreaterThan {
    param($Value, $Threshold, [string]$Message)
    if ($Value -le $Threshold) {
        $msg = if ($Message) { $Message } else { "Expected $Value to be greater than $Threshold" }
        throw $msg
    }
}

function Assert-LessThan {
    param($Value, $Threshold, [string]$Message)
    if ($Value -ge $Threshold) {
        $msg = if ($Message) { $Message } else { "Expected $Value to be less than $Threshold" }
        throw $msg
    }
}

#endregion

#region Sandbox Helpers

function Start-Sandbox {
    param([int]$TimeoutSeconds = 60)

    # Clean up old signals
    Remove-Item $script:Config.ReadySignal -Force -ErrorAction SilentlyContinue
    Remove-Item $script:Config.ShutdownSignal -Force -ErrorAction SilentlyContinue
    Remove-Item $script:Config.ServerTrigger -Force -ErrorAction SilentlyContinue
    Remove-Item $script:Config.AppTrigger -Force -ErrorAction SilentlyContinue

    # Launch sandbox
    $sandboxProcess = Start-Process $script:Config.SandboxConfig -PassThru

    # Wait for ready signal
    $startTime = Get-Date
    while (((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path $script:Config.ReadySignal) {
            $data = Get-Content $script:Config.ReadySignal -Raw | ConvertFrom-Json
            return @{
                Process = $sandboxProcess
                ServerPid = $data.server_pid
                AppPid = $data.app_pid
                Timestamp = $data.timestamp
            }
        }
        Start-Sleep -Milliseconds 500
    }

    throw "Sandbox failed to start within ${TimeoutSeconds}s"
}

function Stop-Sandbox {
    # Send shutdown signal
    Set-Content -Path $script:Config.ShutdownSignal -Value (Get-Date).ToString("o")
    Start-Sleep -Seconds 2

    # Force close any remaining sandbox process
    Get-Process | Where-Object { $_.ProcessName -match "WindowsSandbox" } | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Get-SandboxStatus {
    if (Test-Path $script:Config.ReadySignal) {
        return Get-Content $script:Config.ReadySignal -Raw | ConvertFrom-Json
    }
    return $null
}

function Send-ServerTrigger {
    $triggerTmp = "$($script:Config.ServerTrigger).tmp"
    Set-Content -Path $triggerTmp -Value (Get-Date).ToString("o")
    Move-Item -Path $triggerTmp -Destination $script:Config.ServerTrigger -Force
}

function Send-AppTrigger {
    $triggerTmp = "$($script:Config.AppTrigger).tmp"
    Set-Content -Path $triggerTmp -Value (Get-Date).ToString("o")
    Move-Item -Path $triggerTmp -Destination $script:Config.AppTrigger -Force
}

function Wait-ForReload {
    param([string]$OriginalPid, [int]$TimeoutSeconds = 30)

    $startTime = Get-Date
    while (((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
        $status = Get-SandboxStatus
        if ($status -and $status.server_pid -ne $OriginalPid) {
            return $status
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Get-BootstrapLog {
    param([int]$Lines = 50)
    if (Test-Path $script:Config.BootstrapLog) {
        return Get-Content $script:Config.BootstrapLog -Tail $Lines
    }
    return @()
}

#endregion

#region Export

Export-ModuleMember -Function @(
    'Start-TestRun', 'Complete-TestRun', 'Describe', 'It',
    'Assert-True', 'Assert-False', 'Assert-Equal', 'Assert-NotEqual',
    'Assert-FileExists', 'Assert-FileNotExists', 'Assert-GreaterThan', 'Assert-LessThan',
    'Start-Sandbox', 'Stop-Sandbox', 'Get-SandboxStatus',
    'Send-ServerTrigger', 'Send-AppTrigger', 'Wait-ForReload', 'Get-BootstrapLog'
) -Variable 'Config'

#endregion
