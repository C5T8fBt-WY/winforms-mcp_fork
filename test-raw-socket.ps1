param(
    [string]$SandboxIP = "172.24.135.90",
    [int]$Port = 9999
)

$ErrorActionPreference = "Stop"

# Try with raw System.Net.Sockets
$socket = New-Object System.Net.Sockets.Socket(
    [System.Net.Sockets.AddressFamily]::InterNetwork,
    [System.Net.Sockets.SocketType]::Stream,
    [System.Net.Sockets.ProtocolType]::Tcp
)

$socket.ReceiveTimeout = 15000
$socket.SendTimeout = 5000
$socket.NoDelay = $true

Write-Host "Connecting with raw socket to ${SandboxIP}:${Port}..."
$socket.Connect($SandboxIP, $Port)
Write-Host "Connected!" -ForegroundColor Green

$request = '{"jsonrpc":"2.0","method":"tools/list","params":{},"id":1}' + "`n"
$requestBytes = [System.Text.Encoding]::UTF8.GetBytes($request)

Write-Host "Sending $($requestBytes.Length) bytes: $request"
$bytesSent = $socket.Send($requestBytes)
Write-Host "Sent $bytesSent bytes"

# Poll for data availability
Write-Host "Polling for response..."
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    if ($socket.Available -gt 0) {
        Write-Host "Data available: $($socket.Available) bytes" -ForegroundColor Green
        break
    }
    if ($i % 4 -eq 0) {
        Write-Host "Waiting... ($i/30)"
    }
}

if ($socket.Available -gt 0) {
    $buffer = New-Object byte[] 8192
    $bytesReceived = $socket.Receive($buffer)
    $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesReceived)
    Write-Host "Response ($bytesReceived bytes):" -ForegroundColor Green
    Write-Host $response.Substring(0, [Math]::Min(800, $response.Length))
} else {
    Write-Host "No data received after 15s" -ForegroundColor Yellow
    Write-Host "Socket state: Connected=$($socket.Connected)"
}

$socket.Close()
Write-Host "Done"
