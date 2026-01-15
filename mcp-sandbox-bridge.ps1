# MCP Sandbox Bridge
# Bridges stdio (Claude Code MCP) to TCP (sandbox server)
# Handles JSON-RPC 2.0: requests (with id) expect responses, notifications (no id) don't
#
# Usage: Called automatically by Claude Code via MCP configuration
# The sandbox must be running with the MCP server listening on TCP

param(
    [string]$ServerIP,
    [int]$Port = 9999
)

$ErrorActionPreference = "SilentlyContinue"

# Read TCP IP from ready signal if not provided
if ([string]::IsNullOrEmpty($ServerIP)) {
    $readySignal = "C:\TransportTest\Shared\mcp-ready.signal"
    if (Test-Path $readySignal) {
        try {
            $signal = Get-Content $readySignal -Raw | ConvertFrom-Json
            $ServerIP = $signal.tcp_ip
        } catch {}
    }
    # Fallback to common sandbox IP
    if ([string]::IsNullOrEmpty($ServerIP)) {
        $ServerIP = "172.29.16.229"
    }
}

try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $tcpClient.Connect($ServerIP, $Port)
    $tcpClient.ReceiveTimeout = 30000
    $tcpClient.SendTimeout = 30000
    $stream = $tcpClient.GetStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true

    # Keep connection alive and forward messages bidirectionally
    while ($tcpClient.Connected) {
        # Check for input from stdin (non-blocking would be better but PS doesn't support it well)
        $line = [Console]::In.ReadLine()

        if ($null -eq $line) {
            # EOF on stdin, exit gracefully
            break
        }

        if ($line.Length -gt 0) {
            # Forward to TCP server
            $writer.WriteLine($line)
            $writer.Flush()

            # Check if this is a notification (no "id" field) - notifications don't get responses
            # JSON-RPC 2.0: requests have "id", notifications don't
            $isNotification = $false
            try {
                $json = $line | ConvertFrom-Json -ErrorAction Stop
                if (-not ($json.PSObject.Properties.Name -contains "id")) {
                    $isNotification = $true
                }
            } catch {
                # If we can't parse, assume it's a request and wait for response
            }

            # Only wait for response if this was a request (not a notification)
            if (-not $isNotification) {
                $response = $reader.ReadLine()
                if ($null -ne $response) {
                    [Console]::Out.WriteLine($response)
                    [Console]::Out.Flush()
                }
            }
        }
    }
}
catch {
    [Console]::Error.WriteLine("Bridge error: $_")
    exit 1
}
finally {
    if ($reader) { $reader.Dispose() }
    if ($writer) { $writer.Dispose() }
    if ($stream) { $stream.Dispose() }
    if ($tcpClient) { $tcpClient.Close() }
}
