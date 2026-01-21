Param(
    [string]$Filter,
    [string]$Coverage
)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$dest = 'C:\temp\winforms-mcp'

Write-Host "Syncing source from $source to $dest (Clean)..." -ForegroundColor Gray

# Clean destination
if (Test-Path $dest) {
    Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null

# Copy project files
Copy-Item -Recurse "$source\src" $dest
Copy-Item -Recurse "$source\tests" $dest
Copy-Item "$source\Rhombus.WinFormsMcp.sln" $dest
Copy-Item "$source\Directory.Build.props" $dest -ErrorAction SilentlyContinue
Copy-Item "$source\Directory.Packages.props" $dest -ErrorAction SilentlyContinue

cd $dest

Write-Host "Running unit tests..." -ForegroundColor Cyan

$testArgs = @('test', 'tests/Rhombus.WinFormsMcp.Tests', '--logger', 'console;verbosity=minimal', '-v', 'm', '/p:WarningLevel=0')

if ($Filter) {
    $testArgs += '--filter'
    $testArgs += $Filter
}

if ($Coverage -eq 'true') {
    $testArgs += '--collect:"Code Coverage;Format=Cobertura"'
    Write-Host "Code coverage enabled." -ForegroundColor Yellow
}

dotnet @testArgs

if ($Coverage -eq 'true') {
    $coverageFiles = Get-ChildItem -Path 'tests/Rhombus.WinFormsMcp.Tests/TestResults' -Recurse -Filter '*.cobertura.xml'
    if ($coverageFiles) {
        $file = $coverageFiles[0].FullName
        Write-Host "Coverage file generated at: $file" -ForegroundColor Green

        # Copy results back to source
        $targetReport = Join-Path $source "coverage_report.xml"
        Copy-Item $file $targetReport -Force
        Write-Host "Copied coverage results back to: $targetReport" -ForegroundColor Cyan

        if (Get-Command 'reportgenerator' -ErrorAction SilentlyContinue) {
            reportgenerator -reports:$file -targetdir:'tests/Rhombus.WinFormsMcp.Tests/TestResults/Report' -reporttypes:Html
            Write-Host "Report generated at: $dest\tests\Rhombus.WinFormsMcp.Tests\TestResults\Report\index.html" -ForegroundColor Green
        }
    } else {
        Write-Host "No Cobertura coverage file found!" -ForegroundColor Red
        Get-ChildItem -Path 'tests/Rhombus.WinFormsMcp.Tests/TestResults' -Recurse | ForEach-Object { Write-Host "  Found: $($_.FullName)" -ForegroundColor Gray }
    }
}
