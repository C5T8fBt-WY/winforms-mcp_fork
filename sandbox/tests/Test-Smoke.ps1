# Test-Smoke.ps1
# Basic smoke tests - verify sandbox launches and basic functionality works

param([switch]$Standalone)

if ($Standalone) {
    . "$PSScriptRoot\TestFramework.ps1"
    Start-TestRun -Name "Smoke Tests"
}

Describe "Smoke Tests" {

    It "Setup files exist" {
        Assert-FileExists $Config.SandboxConfig "sandbox-dev.wsb not found"
        Assert-FileExists "$($Config.ServerPath)\Rhombus.WinFormsMcp.Server.exe" "Server exe not found"
        Assert-FileExists "$($Config.ServerPath)\bootstrap.ps1" "bootstrap.ps1 not found"
    }

    It ".NET runtime is available" {
        $dotnetExe = "C:\WinFormsMcpSandboxWorkspace\DotNet\dotnet.exe"
        Assert-FileExists $dotnetExe ".NET runtime not found"
    }

    It "Sandbox launches and becomes ready" {
        $sandbox = Start-Sandbox -TimeoutSeconds 90
        Assert-True ($null -ne $sandbox) "Sandbox failed to start"
        Assert-True ($sandbox.ServerPid -gt 0) "Server PID not set"
        $script:SandboxInstance = $sandbox
    }

    It "Ready signal contains valid data" {
        $status = Get-SandboxStatus
        Assert-True ($null -ne $status) "Ready signal not found"
        Assert-True ($null -ne $status.timestamp) "Missing timestamp"
        Assert-True ($null -ne $status.server_pid) "Missing server_pid"
        Assert-True ($null -ne $status.server_dir) "Missing server_dir"
    }

    It "Bootstrap log is being written" {
        $log = Get-BootstrapLog -Lines 10
        Assert-GreaterThan $log.Count 0 "Bootstrap log is empty"
    }

    It "Shutdown signal stops sandbox gracefully" {
        Stop-Sandbox
        Start-Sleep -Seconds 3
        # Ready signal should be cleaned up
        Assert-FileNotExists $Config.ReadySignal "Ready signal not cleaned up"
    }
}

if ($Standalone) {
    $results = Complete-TestRun
    exit $(if ($results.Failed -gt 0) { 1 } else { 0 })
}
