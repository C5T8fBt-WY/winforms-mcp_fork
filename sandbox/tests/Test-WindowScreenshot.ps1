# Test-WindowScreenshot.ps1
# Tests the window-specific screenshot functionality and coordinate click verification
#
# Prerequisites:
# - Sandbox running with MCP server on TCP port 9999
# - Test app running in sandbox
#
# Usage:
# From host (Windows): .\sandbox\tests\Test-WindowScreenshot.ps1

param(
    [string]$ServerIP = "",  # Auto-detect from mcp-ready.signal if not specified
    [int]$Port = 9999,
    [string]$SharedFolder = "C:\WinFormsMcpSandboxWorkspace\Shared"
)

$ErrorActionPreference = "Stop"

# Auto-detect server IP from signal file
if ([string]::IsNullOrEmpty($ServerIP)) {
    $signalFile = Join-Path $SharedFolder "mcp-ready.signal"
    if (Test-Path $signalFile) {
        $signal = Get-Content $signalFile | ConvertFrom-Json
        $ServerIP = $signal.sandbox_ip
        Write-Host "Auto-detected sandbox IP: $ServerIP" -ForegroundColor Gray
    } else {
        Write-Host "ERROR: Cannot find mcp-ready.signal. Is the sandbox running?" -ForegroundColor Red
        exit 1
    }
}

Write-Host "=== Window Screenshot & Coordinate Test ===" -ForegroundColor Cyan
Write-Host "Server: $ServerIP`:$Port"
Write-Host ""

# Connect to MCP server
try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $tcpClient.Connect($ServerIP, $Port)
    $stream = $tcpClient.GetStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    Write-Host "Connected to MCP server" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Failed to connect - $_" -ForegroundColor Red
    exit 1
}

$requestId = 1

function Send-Request {
    param([string]$method, [hashtable]$params = @{})

    $request = @{
        jsonrpc = "2.0"
        id = $script:requestId++
        method = $method
        params = $params
    } | ConvertTo-Json -Depth 10 -Compress

    $writer.WriteLine($request)
    $response = $reader.ReadLine()
    $parsed = $response | ConvertFrom-Json

    if ($parsed.result) {
        $inner = $parsed.result.content[0].text | ConvertFrom-Json
        return $inner
    }
    return $parsed
}

try {
    # Test 1: Take window screenshot
    Write-Host ""
    Write-Host "Test 1: Window Screenshot with Bounds" -ForegroundColor Yellow
    Write-Host "--------------------------------------"

    $screenshotPath = "C:\Shared\window-test.png"
    $result = Send-Request -method "tools/call" -params @{
        name = "take_screenshot"
        arguments = @{
            outputPath = $screenshotPath
            windowTitle = "WinForms MCP Test"
        }
    }

    if ($result.success -eq $false) {
        Write-Host "FAIL: Screenshot failed - $($result.error)" -ForegroundColor Red
        exit 1
    }

    Write-Host "Screenshot saved to: $screenshotPath" -ForegroundColor Green

    # Check if window bounds are returned
    if ($result.result.window) {
        $window = $result.result.window
        Write-Host "Window bounds returned:" -ForegroundColor Green
        Write-Host "  Handle: $($window.handle)"
        Write-Host "  Title: $($window.title)"
        Write-Host "  Bounds: x=$($window.bounds.x), y=$($window.bounds.y), w=$($window.bounds.width), h=$($window.bounds.height)"

        $windowX = $window.bounds.x
        $windowY = $window.bounds.y
    } else {
        Write-Host "FAIL: No window bounds in response!" -ForegroundColor Red
        Write-Host "Response: $($result | ConvertTo-Json -Depth 5)"
        exit 1
    }

    Write-Host ""
    Write-Host "Test 2: Click Coordinate Test Target" -ForegroundColor Yellow
    Write-Host "-------------------------------------"
    Write-Host "Target is at client coords (700, 350), 10x10 pixels"
    Write-Host "Clicking center at (705, 355) relative to window..."

    # Click the coordinate test target using window-relative coordinates
    # Target is at (700, 350) in client coords, 10x10 pixels
    # Click center at (705, 355)
    $result = Send-Request -method "tools/call" -params @{
        name = "mouse_click"
        arguments = @{
            windowTitle = "WinForms MCP Test"
            x = 705
            y = 355
        }
    }

    if ($result.success -eq $false) {
        Write-Host "FAIL: Click failed - $($result.error)" -ForegroundColor Red
    } else {
        Write-Host "Click sent successfully" -ForegroundColor Green
    }

    Start-Sleep -Milliseconds 300

    # Test 3: Take another screenshot to verify the target turned green
    Write-Host ""
    Write-Host "Test 3: Verify Target Changed Color" -ForegroundColor Yellow
    Write-Host "------------------------------------"

    $verifyPath = "C:\Shared\window-verify.png"
    $result = Send-Request -method "tools/call" -params @{
        name = "take_screenshot"
        arguments = @{
            outputPath = $verifyPath
            windowTitle = "WinForms MCP Test"
        }
    }

    Write-Host "Verification screenshot saved to: $verifyPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "Check the screenshots:" -ForegroundColor Cyan
    Write-Host "  1. $screenshotPath - should show RED target at (700, 350)"
    Write-Host "  2. $verifyPath - should show GREEN target (if click worked)"
    Write-Host ""

    # Test 4: Test desktop screenshot still works
    Write-Host "Test 4: Desktop Screenshot (no window param)" -ForegroundColor Yellow
    Write-Host "---------------------------------------------"

    $desktopPath = "C:\Shared\desktop-test.png"
    $result = Send-Request -method "tools/call" -params @{
        name = "take_screenshot"
        arguments = @{
            outputPath = $desktopPath
        }
    }

    if ($result.success -eq $false) {
        Write-Host "FAIL: Desktop screenshot failed - $($result.error)" -ForegroundColor Red
    } else {
        Write-Host "Desktop screenshot saved to: $desktopPath" -ForegroundColor Green
    }

    # Test 5: Test invalid window handling
    Write-Host ""
    Write-Host "Test 5: Invalid Window Handling" -ForegroundColor Yellow
    Write-Host "--------------------------------"

    $result = Send-Request -method "tools/call" -params @{
        name = "take_screenshot"
        arguments = @{
            outputPath = "C:\Shared\should-fail.png"
            windowTitle = "NonExistent Window 12345"
        }
    }

    if ($result.success -eq $false) {
        Write-Host "PASS: Correctly returned error for non-existent window" -ForegroundColor Green
        Write-Host "  Error: $($result.error)"
    } else {
        Write-Host "FAIL: Should have failed for non-existent window" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "=== Tests Complete ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Manual verification required:" -ForegroundColor Yellow
    Write-Host "  Open $($SharedFolder.Replace('C:\WinFormsMcpSandboxWorkspace', '')) and check:"
    Write-Host "  - window-test.png: Shows only the test app window with RED target"
    Write-Host "  - window-verify.png: Shows test app window with GREEN target"
    Write-Host "  - desktop-test.png: Shows full desktop"
    Write-Host ""

} finally {
    $tcpClient.Close()
}
