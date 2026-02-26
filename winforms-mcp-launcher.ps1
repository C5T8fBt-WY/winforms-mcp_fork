# winforms-mcp shadow-copy launcher
# 
# Copies the current build output (bin/) to a temporary directory, then runs the server
# from there. This ensures the source build directory is NEVER locked by the running process,
# so `dotnet build` always succeeds without killing the server.
#
# VS Code MCP restart = always picks up the latest build automatically.

$buildDir = Join-Path $PSScriptRoot "src\Rhombus.WinFormsMcp.Server\bin\Release\net8.0-windows"
$tempDir  = [System.IO.Path]::Combine($env:TEMP, "winforms-mcp-server")

if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Mirror build output to temp (fast local copy, no locks acquired on source)
$null = robocopy $buildDir $tempDir /E /NJH /NJS /NFL /NDL

if (-not (Test-Path "$tempDir\C5T8fBtWY.WinFormsMcp.Server.dll")) {
    Write-Error "Shadow copy failed: $tempDir\C5T8fBtWY.WinFormsMcp.Server.dll not found. Build the server first (dotnet build -c Release)."
    exit 1
}

# Pipe stdin/stdout through to VS Code (MCP JSON-RPC over stdio)
& dotnet "$tempDir\C5T8fBtWY.WinFormsMcp.Server.dll"
