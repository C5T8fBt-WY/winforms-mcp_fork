# VDD Driver Selection Research

## Purpose

Select a Virtual Display Driver (VDD) for enabling headless/background automation in Windows Sandbox. The VDD allows UI automation to continue when the sandbox window is minimized or not focused.

## Evaluation Criteria

| Criterion | Weight | Description |
|-----------|--------|-------------|
| UMDF-based | Required | User-mode driver for stability (no BSOD risk) |
| Open source | High | Permissive license for bundling |
| Windows 10/11 | Required | Compatible with target platforms |
| Minimal dependencies | High | Easy to bundle with MCP server |
| Stability | High | Proven in production use |
| Easy installation | Medium | Scriptable via PowerShell |

## Candidates Evaluated

### 1. IddSampleDriver (ge9 fork)

**Repository**: https://github.com/ge9/IddSampleDriver

**Description**: Community fork of Microsoft's IddCx sample driver with easy installation.

| Aspect | Assessment |
|--------|------------|
| License | MIT (permissive) |
| Architecture | UMDF + IddCx (user-mode, stable) |
| Installation | Scoop package or manual pnputil |
| Configuration | option.txt for resolution/monitor count |
| Compatibility | Windows 10/11 |
| Maturity | Well-tested, many forks |

**Pros**:
- MIT license allows bundling
- Runs in Session 0, no user-session components
- Simple installation via `pnputil /add-driver`
- Configurable resolution/refresh rate
- Active community maintenance

**Cons**:
- No HDR support (not needed for automation)
- Requires test-signing or signed certificate

### 2. Parsec VDD

**Repository**: https://github.com/nomi-san/parsec-vdd

**Description**: Virtual display driver from Parsec, with open-source wrappers.

| Aspect | Assessment |
|--------|------------|
| License | Proprietary driver, MIT wrapper |
| Architecture | UMDF + IddCx |
| Installation | Requires Parsec app or VDA wrapper |
| Configuration | API-based (requires ping to keep alive) |
| Compatibility | Windows 10+ |
| Maturity | Production-grade (Parsec commercial) |

**Pros**:
- High quality (4K@240Hz support)
- Up to 16 displays per adapter
- Production-tested by Parsec

**Cons**:
- Driver is proprietary (not bundleable)
- Requires periodic "ping" to keep displays alive
- Dependencies on Parsec ecosystem
- No HDR support

### 3. Virtual Display Driver (VirtualDrivers/itsmikethetech)

**Repository**: https://github.com/VirtualDrivers/Virtual-Display-Driver

**Description**: Community driver with HDR support and custom EDID.

| Aspect | Assessment |
|--------|------------|
| License | MIT |
| Architecture | UMDF + IddCx |
| Installation | Manual or installer |
| Configuration | Custom EDID support |
| Compatibility | Windows 10/11 |
| Maturity | Active development |

**Pros**:
- MIT license
- HDR support (optional)
- Custom EDID for hardware emulation
- 8K resolution support

**Cons**:
- More complex than needed for automation
- Larger footprint

## Selection Decision

**Selected: IddSampleDriver (ge9 fork)**

### Rationale

1. **MIT License**: Can be bundled with MCP server without licensing concerns
2. **Simplicity**: Minimal configuration, just option.txt for resolution
3. **Stability**: UMDF driver runs in Session 0, won't crash user session
4. **Easy Installation**: Single `pnputil /add-driver` command
5. **Proven**: Many forks and active community indicate reliability
6. **Minimal Dependencies**: No external services required

### Installation Plan

```powershell
# In sandbox bootstrap.ps1:

# 1. Import test certificate (for development)
Import-Certificate -FilePath "C:\MCP\vdd\IddSampleDriver.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher

# 2. Install driver
pnputil /add-driver "C:\MCP\vdd\IddSampleDriver.inf" /install

# 3. Wait for display to initialize
Start-Sleep -Seconds 3

# 4. Verify installation
Get-PnpDevice -FriendlyName "*IddSampleDriver*" -Status OK
```

### Configuration (option.txt)

```
1,1920,1080,60
```

Single 1920x1080@60Hz virtual display is sufficient for automation.

## Alternative Consideration

If IddSampleDriver proves problematic, **Virtual Display Driver (VirtualDrivers)** is the backup choice due to its MIT license and active development.

## Driver Acquisition

### Method 1: Scoop (Recommended for Windows hosts)

```powershell
# In elevated PowerShell:
scoop bucket add extras
scoop bucket add nonportable
scoop install iddsampledriver-ge9-np -g
```

### Method 2: Manual Download

1. Download latest release from https://github.com/ge9/IddSampleDriver/releases
2. Extract to a folder (e.g., `C:\VDD\`)
3. Files included:
   - `IddSampleDriver.inf` - Device driver configuration
   - `IddSampleDriver.dll` - User-mode driver binary
   - `IddSampleDriver.cat` - Catalog file (for signed builds)
   - `option.txt` - Monitor configuration

### Configuration (option.txt)

```
# Format: count,width,height,refresh
1,1920,1080,60
```

Must be placed at `C:\IddSampleDriver\option.txt` before driver installation.

### For MCP Server Bundling

The driver files will be included in `publish/vdd/` subdirectory:
- Copy driver files from release
- Include test-signed certificate for sandbox installation
- Bootstrap.ps1 handles installation at sandbox startup

## Sources

- [Microsoft IddCx Overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/indirect-display-driver-model-overview)
- [ge9/IddSampleDriver](https://github.com/ge9/IddSampleDriver)
- [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd)
- [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
- [Parsec VDD Documentation](https://support.parsec.app/hc/en-us/articles/32381178803604-VDD-Overview-Prerequisites-and-Installation)
