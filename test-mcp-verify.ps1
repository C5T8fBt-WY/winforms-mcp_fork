param(
    [string]$SandboxIP = "172.24.136.124",
    [int]$Port = 9999,
    [int]$Timeout = 10000
)

$ErrorActionPreference = "Stop"

function Send-McpRequest {
    param(
        [System.Net.Sockets.TcpClient]$Client,
        [string]$Method,
        [hashtable]$Params = @{},
        [int]$Id = 1
    )

    $stream = $Client.GetStream()
    $writer = New-Object System.IO.StreamWriter($stream)
    $reader = New-Object System.IO.StreamReader($stream)
    $writer.AutoFlush = $true

    $request = @{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
        id = $Id
    } | ConvertTo-Json -Compress

    Write-Host "  Sending: $request" -ForegroundColor Gray
    $writer.WriteLine($request)

    $responseTask = $reader.ReadLineAsync()
    if ($responseTask.Wait($Timeout)) {
        $response = $responseTask.Result
        return $response | ConvertFrom-Json
    } else {
        throw "Response timeout"
    }
}

function Connect-Sandbox {
    $client = New-Object System.Net.Sockets.TcpClient
    $client.ReceiveTimeout = $Timeout
    $client.SendTimeout = $Timeout

    Write-Host "Connecting to $SandboxIP`:$Port..."
    $connectResult = $client.BeginConnect($SandboxIP, $Port, $null, $null)
    $waitHandle = $connectResult.AsyncWaitHandle

    if (-not $waitHandle.WaitOne($Timeout, $false)) {
        throw "Connection timeout"
    }

    $client.EndConnect($connectResult)
    Write-Host "Connected!" -ForegroundColor Green
    return $client
}

try {
    # Test A: get_ui_tree via tools/call
    Write-Host "`n=== Test A: get_ui_tree ===" -ForegroundColor Cyan
    $client = Connect-Sandbox
    $result = Send-McpRequest -Client $client -Method "tools/call" -Params @{
        name = "get_ui_tree"
        arguments = @{ maxDepth = 2 }
    }
    $client.Close()

    # MCP tools/call returns result.content[].text as JSON string
    $xml = $null
    if ($result.result -and $result.result.content) {
        foreach ($item in $result.result.content) {
            if ($item.type -eq "text") {
                try {
                    $parsed = $item.text | ConvertFrom-Json
                    if ($parsed.success -and $parsed.result -and $parsed.result.xml) {
                        $xml = $parsed.result.xml
                    }
                } catch {
                    # Check if it's raw XML
                    if ($item.text -match '<tree') {
                        $xml = $item.text
                    }
                }
                break
            }
        }
    }

    if ($xml) {
        Write-Host "  Response length: $($xml.Length) chars"

        if ($xml -match 'runtimeId=') { Write-Host "  [PASS] runtimeId attribute found" -ForegroundColor Green }
        else { Write-Host "  [FAIL] runtimeId attribute NOT found" -ForegroundColor Red }

        if ($xml -match 'hasFocus=') { Write-Host "  [PASS] hasFocus attribute found" -ForegroundColor Green }
        else { Write-Host "  [WARN] hasFocus not found (may be no focused element)" -ForegroundColor Yellow }

        if ($xml -match 'pid=') { Write-Host "  [PASS] pid attribute found" -ForegroundColor Green }
        else { Write-Host "  [FAIL] pid attribute NOT found" -ForegroundColor Red }

        if ($xml -match 'nativeWindowHandle=') { Write-Host "  [PASS] nativeWindowHandle attribute found" -ForegroundColor Green }
        else { Write-Host "  [FAIL] nativeWindowHandle attribute NOT found" -ForegroundColor Red }

        # Show sample
        Write-Host "`n  Sample XML (first 600 chars):" -ForegroundColor Gray
        Write-Host $xml.Substring(0, [Math]::Min(600, $xml.Length))
    } else {
        Write-Host "  [FAIL] No XML in response" -ForegroundColor Red
        Write-Host "  Response: $($result | ConvertTo-Json -Depth 5)"
    }

    # Test B: get_element_at_point via tools/call
    Write-Host "`n=== Test B: get_element_at_point ===" -ForegroundColor Cyan
    $client = Connect-Sandbox
    $result = Send-McpRequest -Client $client -Method "tools/call" -Params @{
        name = "get_element_at_point"
        arguments = @{ x = 100; y = 100 }
    } -Id 2
    $client.Close()

    # Parse result from content
    $elementResult = $null
    if ($result.result -and $result.result.content) {
        foreach ($item in $result.result.content) {
            if ($item.type -eq "text") {
                try {
                    $elementResult = $item.text | ConvertFrom-Json
                } catch {
                    $elementResult = @{ text = $item.text }
                }
                break
            }
        }
    }

    if ($elementResult -and $elementResult.success) {
        Write-Host "  [PASS] Element found at (100, 100)" -ForegroundColor Green
        Write-Host "  ControlType: $($elementResult.controlType)"
        Write-Host "  Name: $($elementResult.name)"
        Write-Host "  RuntimeId: $($elementResult.runtimeId)"
        Write-Host "  PID: $($elementResult.pid)"
        Write-Host "  ProcessName: $($elementResult.processName)"
    } elseif ($elementResult) {
        Write-Host "  [INFO] Response received" -ForegroundColor Yellow
        Write-Host "  Response: $($elementResult | ConvertTo-Json -Depth 3)"
    } else {
        Write-Host "  [FAIL] Error in response" -ForegroundColor Red
        Write-Host "  Response: $($result | ConvertTo-Json -Depth 5)"
    }

    # Test C: take_screenshot with returnBase64 via tools/call
    Write-Host "`n=== Test C: take_screenshot with returnBase64 ===" -ForegroundColor Cyan
    $client = Connect-Sandbox
    $result = Send-McpRequest -Client $client -Method "tools/call" -Params @{
        name = "take_screenshot"
        arguments = @{ returnBase64 = $true }
    } -Id 3
    $client.Close()

    # Parse result from content
    $screenshotResult = $null
    if ($result.result -and $result.result.content) {
        foreach ($item in $result.result.content) {
            if ($item.type -eq "text") {
                try {
                    $screenshotResult = $item.text | ConvertFrom-Json
                } catch {
                    $screenshotResult = @{ text = $item.text }
                }
                break
            }
        }
    }

    if ($screenshotResult -and $screenshotResult.base64) {
        $base64 = $screenshotResult.base64
        Write-Host "  [PASS] Base64 screenshot received" -ForegroundColor Green
        Write-Host "  Format: $($screenshotResult.format)"
        Write-Host "  Base64 length: $($base64.Length) chars"
        Write-Host "  Base64 preview: $($base64.Substring(0, [Math]::Min(80, $base64.Length)))..."

        # Validate it's valid base64 PNG
        if ($base64.StartsWith("iVBOR")) {
            Write-Host "  [PASS] Valid PNG header detected" -ForegroundColor Green
        } else {
            Write-Host "  [WARN] Unexpected base64 header" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  [FAIL] No base64 in response" -ForegroundColor Red
        Write-Host "  Response: $($result | ConvertTo-Json -Depth 5)"
    }

    Write-Host "`n=== All tests complete ===" -ForegroundColor Cyan

} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
}
