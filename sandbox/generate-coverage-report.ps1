# Generate HTML coverage report from cobertura XML
# Usage: .\generate-coverage-report.ps1
#
# Prerequisites:
#   dotnet tool install -g dotnet-reportgenerator-globaltool
#
# This reads coverage.cobertura.xml from the shared folder and generates
# an HTML report in the coverage-report subdirectory.

param(
    [string]$SharedFolder = "C:\WinFormsMcpSandboxWorkspace\Shared",
    [string]$OutputFolder = "C:\WinFormsMcpSandboxWorkspace\coverage-report"
)

$CoverageFile = Join-Path $SharedFolder "coverage.cobertura.xml"

if (!(Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found at $CoverageFile"
    Write-Host ""
    Write-Host "To generate coverage:"
    Write-Host "  1. Start the sandbox (it's configured for coverage)"
    Write-Host "  2. Exercise MCP tools to generate coverage data"
    Write-Host "  3. Stop the server (coverage is written on exit)"
    Write-Host "  4. Run this script"
    exit 1
}

# Check if reportgenerator is available
$reportGen = Get-Command reportgenerator -ErrorAction SilentlyContinue
if (!$reportGen) {
    Write-Error "reportgenerator not found. Install it with:"
    Write-Host "  dotnet tool install -g dotnet-reportgenerator-globaltool"
    exit 1
}

Write-Host "Generating coverage report..."
Write-Host "  Input: $CoverageFile"
Write-Host "  Output: $OutputFolder"

# Create output directory
if (!(Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
}

# Generate HTML report
reportgenerator `
    -reports:$CoverageFile `
    -targetdir:$OutputFolder `
    -reporttypes:Html

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Coverage report generated successfully!" -ForegroundColor Green
    Write-Host "Open: $OutputFolder\index.html"

    # Optionally open in browser
    $indexPath = Join-Path $OutputFolder "index.html"
    if (Test-Path $indexPath) {
        Start-Process $indexPath
    }
} else {
    Write-Error "Failed to generate coverage report"
    exit 1
}
