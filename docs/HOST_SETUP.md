# Host Machine Setup Guide

One-time setup requirements for the Windows host machine to enable headless/background UI automation.

## Overview

By default, Windows optimizes minimized windows by stopping their rendering. This breaks UI automation for minimized sandbox sessions. This guide covers the setup needed to enable automation when the sandbox window is minimized.

---

## Registry Configuration

### RemoteDesktop_SuppressWhenMinimized

This registry key prevents Windows from suppressing rendering when the window is minimized.

**Key**: `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Terminal Server Client`
**Name**: `RemoteDesktop_SuppressWhenMinimized`
**Type**: DWORD
**Value**: 2

### Automated Setup (PowerShell)

```powershell
# Run as Administrator

# Create the registry key if it doesn't exist
$regPath = "HKCU:\SOFTWARE\Microsoft\Terminal Server Client"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}

# Set the value
Set-ItemProperty -Path $regPath -Name "RemoteDesktop_SuppressWhenMinimized" -Value 2 -Type DWord

# Verify
$value = Get-ItemProperty -Path $regPath -Name "RemoteDesktop_SuppressWhenMinimized"
Write-Host "Registry set to: $($value.RemoteDesktop_SuppressWhenMinimized)"
```

### Manual Setup (Registry Editor)

1. Press `Win+R`, type `regedit`, press Enter
2. Navigate to: `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Terminal Server Client`
3. If the key doesn't exist, right-click and create it
4. Right-click in the right pane → New → DWORD (32-bit) Value
5. Name: `RemoteDesktop_SuppressWhenMinimized`
6. Double-click, set Value data to `2`
7. Click OK

---

## Virtual Display Driver (VDD)

For true headless operation (no monitor connected), a Virtual Display Driver provides a render target.

### Why VDD is Needed

Without a display:
- Windows skips rendering
- Screenshots return black images
- Some UI elements don't respond

With VDD:
- A virtual monitor is created
- Rendering works normally
- Screenshots and automation work

### VDD Setup

**Note**: VDD requires test-mode signing or an EV code-signed driver for production.

1. **Download a VDD driver** (e.g., usbmmidd, IddSampleDriver)
2. **Install the test certificate** (for development):
   ```powershell
   Import-Certificate -FilePath "C:\VDD\test-cert.cer" -CertStoreLocation Cert:\LocalMachine\Root
   ```
3. **Install the driver**:
   ```powershell
   pnputil /add-driver "C:\VDD\vdd.inf" /install
   ```
4. **Verify** in Device Manager → Display adapters

---

## Verification

### Test Registry Setting

```powershell
# Check if registry key is set correctly
$regPath = "HKCU:\SOFTWARE\Microsoft\Terminal Server Client"
$value = Get-ItemProperty -Path $regPath -Name "RemoteDesktop_SuppressWhenMinimized" -ErrorAction SilentlyContinue

if ($value -and $value.RemoteDesktop_SuppressWhenMinimized -eq 2) {
    Write-Host "OK: RemoteDesktop_SuppressWhenMinimized is set to 2" -ForegroundColor Green
} else {
    Write-Host "MISSING: Registry key not set. Run setup script." -ForegroundColor Red
}
```

### Test Minimized Automation

1. Launch an application
2. Minimize the window
3. Take a screenshot via MCP:
   ```json
   { "tool": "take_screenshot", "args": { "windowTitle": "MyApp" } }
   ```
4. Check if screenshot shows actual UI (not black)

### Test VDD (if installed)

```powershell
# Check display count
Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public class Display {
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
}
"@

$SM_CMONITORS = 80
$monitors = [Display]::GetSystemMetrics($SM_CMONITORS)
Write-Host "Display count: $monitors"
```

---

## Troubleshooting

### Automation Fails When Minimized

**Symptom**: get_ui_tree returns empty or stale data when sandbox is minimized

**Solutions**:
1. Verify registry key is set (see Verification section)
2. Log off and log back on (registry changes need session restart)
3. Consider using VDD for completely headless operation

### Screenshots Return Black Images

**Symptom**: take_screenshot returns all-black PNG when minimized

**Solutions**:
1. Check registry key setting
2. Try maximizing and re-minimizing the window
3. Use VDD if running without a physical monitor

### Registry Key Doesn't Persist

**Symptom**: Key reverts after reboot

**Solutions**:
1. Check if Group Policy is overriding the setting
2. Run `gpresult /h report.html` to check policies
3. Contact IT admin if managed machine

### VDD Driver Won't Install

**Symptom**: pnputil fails with signing error

**Solutions**:
1. Enable test signing mode (development only):
   ```cmd
   bcdedit /set testsigning on
   ```
   Reboot required
2. Use an EV code-signed driver for production
3. Check Windows Security → Device security settings

---

## Security Notes

### Registry Modification

The `RemoteDesktop_SuppressWhenMinimized` key only affects rendering behavior for minimized windows. It does not:
- Grant additional permissions
- Open network ports
- Modify security settings

### VDD Considerations

Installing unsigned drivers requires test signing mode, which:
- Displays a watermark on desktop
- May be flagged by security software
- Should only be used in development

For production, use a properly signed VDD from a trusted vendor.

---

## Quick Setup Script

Save as `setup-host.ps1` and run as Administrator:

```powershell
#Requires -RunAsAdministrator

Write-Host "=== WinForms MCP Host Setup ===" -ForegroundColor Cyan

# 1. Set registry key
$regPath = "HKCU:\SOFTWARE\Microsoft\Terminal Server Client"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
    Write-Host "Created registry path" -ForegroundColor Yellow
}

Set-ItemProperty -Path $regPath -Name "RemoteDesktop_SuppressWhenMinimized" -Value 2 -Type DWord
Write-Host "Set RemoteDesktop_SuppressWhenMinimized = 2" -ForegroundColor Green

# 2. Verify
$value = Get-ItemProperty -Path $regPath -Name "RemoteDesktop_SuppressWhenMinimized"
if ($value.RemoteDesktop_SuppressWhenMinimized -eq 2) {
    Write-Host "Verification: OK" -ForegroundColor Green
} else {
    Write-Host "Verification: FAILED" -ForegroundColor Red
}

Write-Host ""
Write-Host "Setup complete. You may need to log off and log back on for changes to take effect." -ForegroundColor Cyan
```
