# GUI Test Script for WinForms MCP
# Launches TestApp and exercises it via the MCP server

param(
    [switch]$KeepOpen,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

# Paths
$repoRoot = Split-Path -Parent $PSScriptRoot
$serverExe = Join-Path $repoRoot "src\Rhombus.WinFormsMcp.Server\bin\Debug\net8.0-windows\Rhombus.WinFormsMcp.Server.exe"
$testAppExe = Join-Path $repoRoot "src\Rhombus.WinFormsMcp.TestApp\bin\Debug\net8.0-windows\Rhombus.WinFormsMcp.TestApp.exe"

# Build if needed
if (-not (Test-Path $serverExe) -or -not (Test-Path $testAppExe)) {
    Write-Host "Building solution..." -ForegroundColor Yellow
    Push-Location $repoRoot
    dotnet build Rhombus.WinFormsMcp.sln
    Pop-Location
}

# Test results tracking
$script:testsPassed = 0
$script:testsFailed = 0
$script:testResults = @()

function Write-TestResult {
    param($Name, $Passed, $Message = "")
    if ($Passed) {
        Write-Host "  [PASS] $Name" -ForegroundColor Green
        $script:testsPassed++
    } else {
        Write-Host "  [FAIL] $Name - $Message" -ForegroundColor Red
        $script:testsFailed++
    }
    $script:testResults += @{ Name = $Name; Passed = $Passed; Message = $Message }
}

# Start MCP server process
Write-Host "`n=== Starting MCP Server ===" -ForegroundColor Cyan
$serverProcess = New-Object System.Diagnostics.Process
$serverProcess.StartInfo.FileName = $serverExe
$serverProcess.StartInfo.UseShellExecute = $false
$serverProcess.StartInfo.RedirectStandardInput = $true
$serverProcess.StartInfo.RedirectStandardOutput = $true
$serverProcess.StartInfo.RedirectStandardError = $true
$serverProcess.StartInfo.CreateNoWindow = $true
$serverProcess.Start() | Out-Null

$stdin = $serverProcess.StandardInput
$stdout = $serverProcess.StandardOutput

# Give server time to initialize
Start-Sleep -Milliseconds 500

# Send initialize request
$initRequest = @{
    jsonrpc = "2.0"
    id = 0
    method = "initialize"
    params = @{
        protocolVersion = "2024-11-05"
        capabilities = @{}
        clientInfo = @{ name = "gui-test-script"; version = "1.0" }
    }
} | ConvertTo-Json -Compress

$stdin.WriteLine($initRequest)
$stdin.Flush()
$initResponse = $stdout.ReadLine()
Write-Host "MCP Server initialized" -ForegroundColor Green

if ($Verbose) {
    Write-Host "  Init response: $initResponse" -ForegroundColor DarkGray
}

# Send notifications/initialized to complete MCP handshake
# This is a notification (no id), so no response is expected
$initializedNotification = @{
    jsonrpc = "2.0"
    method = "notifications/initialized"
} | ConvertTo-Json -Compress

$stdin.WriteLine($initializedNotification)
$stdin.Flush()
Write-Host "Sent initialized notification" -ForegroundColor Green

# Small delay to let server process the notification
Start-Sleep -Milliseconds 100

$script:requestId = 1

function Invoke-McpTool {
    param($ToolName, $Arguments = @{})

    $request = @{
        jsonrpc = "2.0"
        id = $script:requestId++
        method = "tools/call"
        params = @{
            name = $ToolName
            arguments = $Arguments
        }
    } | ConvertTo-Json -Depth 10 -Compress

    if ($Verbose) {
        Write-Host "  >> $ToolName" -ForegroundColor DarkGray
    }

    $stdin.WriteLine($request)
    $stdin.Flush()

    $response = $stdout.ReadLine()

    if ($Verbose) {
        Write-Host "  << $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor DarkGray
    }

    $json = $response | ConvertFrom-Json
    if ($json.error) {
        return @{ success = $false; error = $json.error.message }
    }
    if ($json.result -and $json.result.content) {
        return $json.result.content[0].text | ConvertFrom-Json
    }
    return $json
}

$testAppPid = $null

try {
    Write-Host "`n=== Test 1: Launch TestApp ===" -ForegroundColor Cyan
    $launchResult = Invoke-McpTool "launch_app" @{ path = $testAppExe }
    $testAppPid = $launchResult.pid
    Write-TestResult "Launch TestApp" ($testAppPid -gt 0) "PID: $testAppPid"

    # Wait for app to fully load
    Start-Sleep -Seconds 2

    Write-Host "`n=== Test 2: Get Window Bounds ===" -ForegroundColor Cyan
    $windowResult = Invoke-McpTool "get_window_bounds" @{ windowTitle = "WinForms MCP Test" }
    Write-TestResult "Get Window Bounds" ($windowResult.success -and $windowResult.width -gt 0) "Size: $($windowResult.width)x$($windowResult.height)"

    Write-Host "`n=== Test 3: List UI Elements ===" -ForegroundColor Cyan
    $elementsResult = Invoke-McpTool "list_elements" @{ windowTitle = "WinForms MCP Test"; maxDepth = 3 }
    $elementCount = if ($elementsResult.elements) { $elementsResult.elements.Count } else { 0 }
    Write-TestResult "List Elements" ($elementsResult.success -and $elementCount -gt 0) "Found $elementCount elements"

    if ($Verbose -and $elementsResult.elements) {
        Write-Host "  Elements found:" -ForegroundColor DarkGray
        $elementsResult.elements | ForEach-Object {
            Write-Host "    - $($_.automationId): $($_.controlType) '$($_.name)'" -ForegroundColor DarkGray
        }
    }

    Write-Host "`n=== Test 4: Find TextBox by AutomationId ===" -ForegroundColor Cyan
    $findResult = Invoke-McpTool "find_element" @{ automationId = "textBox" }
    $textBoxFound = $findResult.success -and $findResult.elementId
    Write-TestResult "Find TextBox" $textBoxFound "ElementId: $($findResult.elementId)"

    Write-Host "`n=== Test 5: Type Text ===" -ForegroundColor Cyan
    if ($textBoxFound) {
        $typeResult = Invoke-McpTool "type_text" @{ elementPath = $findResult.elementId; text = "Hello from MCP test!"; clearFirst = $true }
        Write-TestResult "Type Text" ($typeResult.success -eq $true) ""
    } else {
        Write-TestResult "Type Text" $false "Skipped - TextBox not found"
    }

    Write-Host "`n=== Test 6: Find and Click Button ===" -ForegroundColor Cyan
    $buttonResult = Invoke-McpTool "find_element" @{ automationId = "clickButton" }
    $buttonFound = $buttonResult.success -and $buttonResult.elementId
    Write-TestResult "Find Button" $buttonFound "ElementId: $($buttonResult.elementId)"

    if ($buttonFound) {
        $clickResult = Invoke-McpTool "click_element" @{ elementPath = $buttonResult.elementId }
        Write-TestResult "Click Button" ($clickResult.success -eq $true) ""
        Start-Sleep -Milliseconds 1000

        # Dismiss the message box that appears - look for OK button
        Write-Host "  Looking for message box OK button..." -ForegroundColor DarkGray
        $okResult = Invoke-McpTool "click_by_automation_id" @{ automationId = "2" }  # Standard OK button ID
        if (-not $okResult.success) {
            # Try finding by name
            $okResult = Invoke-McpTool "find_element" @{ name = "OK" }
            if ($okResult.success -and $okResult.elementId) {
                Invoke-McpTool "click_element" @{ elementPath = $okResult.elementId } | Out-Null
            }
        }
        Write-Host "  (Attempted to dismiss message box)" -ForegroundColor DarkGray
    }

    Write-Host "`n=== Test 7: Find and Toggle CheckBox ===" -ForegroundColor Cyan
    $checkResult = Invoke-McpTool "find_element" @{ automationId = "checkBox" }
    $checkFound = $checkResult.success -and $checkResult.elementId
    Write-TestResult "Find CheckBox" $checkFound "ElementId: $($checkResult.elementId)"

    if ($checkFound) {
        $toggleResult = Invoke-McpTool "click_element" @{ elementPath = $checkResult.elementId }
        Write-TestResult "Toggle CheckBox" ($toggleResult.success -eq $true) ""
    }

    Write-Host "`n=== Test 8: Take Screenshot ===" -ForegroundColor Cyan
    $screenshotPath = Join-Path $env:TEMP "mcp-gui-test-$(Get-Date -Format 'yyyyMMdd-HHmmss').png"
    $screenshotResult = Invoke-McpTool "take_screenshot" @{ outputPath = $screenshotPath }
    $screenshotExists = Test-Path $screenshotPath
    Write-TestResult "Take Screenshot" ($screenshotResult.success -or $screenshotExists) "Path: $screenshotPath"

    if ($screenshotExists) {
        $fileSize = (Get-Item $screenshotPath).Length
        Write-Host "  Screenshot size: $fileSize bytes" -ForegroundColor DarkGray
        if (-not $KeepOpen) {
            Remove-Item $screenshotPath -Force
        } else {
            Write-Host "  Screenshot saved: $screenshotPath" -ForegroundColor Yellow
        }
    }

    Write-Host "`n=== Test 9: Find ComboBox ===" -ForegroundColor Cyan
    $comboResult = Invoke-McpTool "find_element" @{ automationId = "comboBox" }
    Write-TestResult "Find ComboBox" ($comboResult.success -and $comboResult.elementId) "ElementId: $($comboResult.elementId)"

    Write-Host "`n=== Test 10: Find ListBox ===" -ForegroundColor Cyan
    $listResult = Invoke-McpTool "find_element" @{ automationId = "listBox" }
    Write-TestResult "Find ListBox" ($listResult.success -and $listResult.elementId) "ElementId: $($listResult.elementId)"

    Write-Host "`n=== Test 11: Check Element State ===" -ForegroundColor Cyan
    if ($checkFound) {
        $stateResult = Invoke-McpTool "check_element_state" @{ elementPath = $checkResult.elementId }
        Write-TestResult "Check Element State" ($stateResult.success) "IsEnabled: $($stateResult.isEnabled), ToggleState: $($stateResult.toggleState)"
    } else {
        Write-TestResult "Check Element State" $false "Skipped - CheckBox not found"
    }

    Write-Host "`n=== Test 12: Focus Window ===" -ForegroundColor Cyan
    $focusResult = Invoke-McpTool "focus_window" @{ windowTitle = "WinForms MCP Test" }
    Write-TestResult "Focus Window" ($focusResult.success) ""

} catch {
    Write-Host "`nError during tests: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
} finally {
    # Cleanup
    if (-not $KeepOpen) {
        Write-Host "`n=== Cleanup ===" -ForegroundColor Cyan

        # Close TestApp
        if ($testAppPid) {
            try {
                $closeResult = Invoke-McpTool "close_app" @{ pid = $testAppPid; force = $true }
                Write-Host "TestApp closed via MCP" -ForegroundColor Green
            } catch {
                # Force kill if MCP close fails
                Stop-Process -Id $testAppPid -Force -ErrorAction SilentlyContinue
                Write-Host "TestApp force-killed" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "`nKeeping TestApp open (PID: $testAppPid)" -ForegroundColor Yellow
    }

    # Stop MCP server
    try {
        $stdin.Close()
        $serverProcess.Kill()
        $serverProcess.Dispose()
    } catch {}
    Write-Host "MCP Server stopped" -ForegroundColor Green
}

# Summary
Write-Host "`n$("=" * 50)" -ForegroundColor Cyan
Write-Host "TEST SUMMARY" -ForegroundColor Cyan
Write-Host ("=" * 50) -ForegroundColor Cyan
Write-Host "Passed: $script:testsPassed" -ForegroundColor Green
Write-Host "Failed: $script:testsFailed" -ForegroundColor $(if ($script:testsFailed -gt 0) { "Red" } else { "Green" })
Write-Host "Total:  $($script:testsPassed + $script:testsFailed)" -ForegroundColor White

if ($script:testsFailed -gt 0) {
    Write-Host "`nFailed tests:" -ForegroundColor Red
    $script:testResults | Where-Object { -not $_.Passed } | ForEach-Object {
        Write-Host "  - $($_.Name): $($_.Message)" -ForegroundColor Red
    }
    exit 1
}

Write-Host "`nAll tests passed!" -ForegroundColor Green
exit 0
