#!/bin/bash
set -e

FILTER=""
COVERAGE="false"

while [[ $# -gt 0 ]]; do
  case $1 in
    -c|--coverage) COVERAGE="true"; shift ;;
    *) FILTER="$1"; shift ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WIN_SCRIPT_PATH=$(wslpath -w "$SCRIPT_DIR/test_runner.ps1")

# Invoke the PowerShell script file directly, passing named arguments
powershell.exe -ExecutionPolicy Bypass -File "$WIN_SCRIPT_PATH" -Filter "$FILTER" -Coverage "$COVERAGE"
