# Advanced Architectures for High-Fidelity Background Automation in Windows Sandbox

**Source**: Gemini Research Spike
**Date**: 2025-01-13
**Topic**: Can user keep control of laptop while agent runs in sandbox?

**Answer**: YES - with proper configuration (VDD + Host Registry + Touch Input)

---

## Executive Summary

Windows Sandbox uses RDP internally for display. When the Sandbox window is minimized, the RDP client signals the guest to stop rendering, breaking automation. The solution requires three layers:

| Layer | Component | Purpose |
|-------|-----------|---------|
| 1. Display | Indirect Display Driver (IDD) | Virtual monitor inside sandbox - keeps DWM rendering |
| 2. Session | Host Registry Key | Prevents RDP throttling when minimized |
| 3. Input | `InjectTouchInput` | Reliable clicks in headless state |

---

## 1. Introduction: The Evolution of Ephemeral Virtualization in Enterprise Automation

The landscape of software testing, malware analysis, and secure development has been fundamentally altered by the advent of containerized desktop environments. Historically, virtualization was the domain of Type 1 (bare-metal) and Type 2 (hosted) hypervisors—heavyweight solutions requiring significant resource allocation, storage management, and maintenance overhead. The introduction of Windows Sandbox represented a paradigm shift toward "ephemeral virtualization," leveraging the host operating system's binaries to construct a pristine, disposable environment in seconds.

However, the utility of Windows Sandbox has traditionally been constrained by its dependency on the Windows Presentation Foundation (WPF) and the Remote Desktop Protocol (RDP) for user interaction. While designed to provide a secure desktop for transient tasks, the architecture inherently prioritizes resource conservation and user attentiveness. When the Sandbox window is minimized or the host session locks, the underlying rendering subsystems suspend operations to conserve GPU and CPU cycles. This behavior, while efficient for a desktop user, poses a catastrophic barrier to automated workflows. Background automation—essential for continuous integration/continuous deployment (CI/CD) pipelines, large-scale UI testing, and stealthy malware behavioral analysis—requires a display pipeline that remains active regardless of the host's state.

This report provides an exhaustive technical analysis of the mechanisms required to transform Windows Sandbox into a robust, headless automation node. We examine the implementation of Indirect Display Drivers (IDD) to decouple rendering from the physical console, the manipulation of RDP session policies via the Windows Registry to override suspension logic, and the utilization of low-level Input Injection APIs to synthesize user interaction in the absence of a physical device.

### 1.1 The Theoretical Basis of Container-Based Isolation

Windows Sandbox utilizes a technology originally developed for Windows Containers, known as "dynamically generated image." Unlike a standard VM that boots from a static Virtual Hard Disk (VHD) containing a full copy of the operating system, Windows Sandbox constructs its file system by creating mutable links to the host's Windows binaries.

This architecture ensures that the Sandbox is always version-aligned with the host, eliminating the "patch gap" often seen in dormant VM images. However, it also implies that the Sandbox is deeply intertwined with the host's kernel scheduler and memory manager. The "Integrated Kernel Scheduler" allows the host to manage the Sandbox's threads as if they were local processes, enabling aggressive resource reclamation. When the Sandbox window is minimized, the host's Desktop Window Manager (DWM) signals the virtualization stack to deprioritize the rendering threads of the guest.

This efficiency mechanism is the primary adversary of background automation. In a standard VM (e.g., Hyper-V or VMware), the guest OS is largely unaware of the host's window state and continues to drive its virtual video card. In Windows Sandbox, the video output is not a simulated VGA card but a synthetic RDP surface. The connection is effectively a local RDP session. Consequently, the Sandbox inherits the bandwidth-saving behaviors of the RDP protocol, which ceases graphical updates when the client window is not visible.

### 1.2 The Business Case for Background Automation

The necessity for overriding these default behaviors arises from critical enterprise use cases:

- **CI/CD Pipeline Integration**: Developers require ephemeral environments to run UI tests (e.g., Selenium, Appium) triggered by code commits. These tests must run in the background on developer workstations or build agents without seizing control of the physical mouse and keyboard.

- **Malware Behavioral Analysis**: Security analysts need to detonate suspicious files in a safe environment. Malware often detects user inactivity or lack of mouse movement (anti-sandbox techniques) and remains dormant. A background automation script simulating human interaction is required to trigger the payload, but this must occur without disrupting the analyst's primary workflow.

- **Robotic Process Automation (RPA)**: Legacy enterprise applications often lack APIs, necessitating UI-based automation. Running these bots in Windows Sandbox ensures a clean state for every execution, preventing data pollution, but requires the bot to function reliably while the host machine is used for other tasks.

---

## 2. The Graphics Subsystem and RDP Protocol Dynamics

The central technical challenge in backgrounding Windows Sandbox lies in the interaction between the Windows Display Driver Model (WDDM) and the Remote Desktop Protocol (RDP).

### 2.1 The Role of the Desktop Window Manager (DWM)

In modern Windows (Windows 10 and 11), the Desktop Window Manager (DWM) is the compositing window manager. It draws the window borders, manages transparency effects, and, crucially, combines the output of various applications into the final image sent to the display. DWM relies on the presence of a "display target"—a monitor or a virtual equivalent.

When a Windows Sandbox session is active and maximized, the DWM inside the guest composes the desktop and sends the bitmap to the RDP server component. The RDP server compresses this and sends it to the host's RDP client (the Sandbox window).

However, when the host window is minimized, the RDP client sends a specific Protocol Data Unit (PDU) to the server indicating a "suppress output" state. This is a legacy optimization designed for low-bandwidth connections. Upon receiving this signal, the guest OS's RDP server instructs the DWM to stop composing frames.

**Consequence for Automation**: With DWM suspended, applications that rely on WM_PAINT messages or GPU-accelerated rendering pipelines (DirectX/OpenGL) stop updating their visual state.

**Consequence for Screen Scraping**: Automation tools that rely on analyzing pixel data (e.g., finding a button by its image) read from the video buffer. When rendering stops, this buffer is either frozen at the last frame or cleared to black, causing the automation script to timeout or fail.

### 2.2 RDP Session Negotiation and Bandwidth Optimization

The mechanism of suppression is deeply rooted in the RDP stack. The client and server negotiate "capabilities" during the handshake. One capability is the "Frame Acknowledge" mechanism. In a minimized state, the client stops acknowledging frames. The server, implementing flow control, fills its outbound buffer and subsequently stops generating new frames to prevent buffer overflow and wasted CPU cycles.

Furthermore, Windows employs a "Fair Share CPU Scheduling" for Remote Desktop Session Hosts (RDSH). While Windows Sandbox is a client OS, it utilizes RDSH technology. When a session is disconnected or minimized, its priority class is lowered. This can cause timing-sensitive automation scripts to desynchronize, as the guest CPU is throttled by the host hypervisor.

### 2.3 Headless Rendering Limitations

"Headless" usually implies running without a monitor. However, Windows is inherently a graphical OS. Even "Server Core" installations have a display driver (typically the basic Microsoft Basic Display Adapter). In the context of Sandbox automation, "headless" refers to the host's lack of visibility of the guest.

The issue is that Windows Sandbox does not utilize a persistent virtual GPU (vGPU) in the same way a standard Hyper-V VM does. A standard VM has a synthetic graphics card (e.g., Hyper-V Video) that exists regardless of whether a viewer is connected. Windows Sandbox's display adapter is transient and tied to the RDP session lifecycle. If the session capability negotiation determines that no display is needed (minimized), the virtual display adapter inside the Sandbox effectively goes to sleep.

This architectural dependency necessitates a two-pronged solution:
- **Host-Side**: Forcing the RDP client to ignore the minimized state (Registry modification).
- **Guest-Side**: Providing a resilient, always-available display adapter (Indirect Display Driver) that operates independently of the RDP session's native adapter.

---

## 3. Indirect Display Drivers (IDD): The Virtualization Enabler

To robustly solve the rendering suspension issue, we must introduce a component that simulates a physical monitor within the Sandbox. This forces the guest OS to maintain an active desktop composition pipeline, regardless of the state of the primary RDP display.

### 3.1 Architecture of User-Mode Display Drivers

Historically, display drivers (XDDM) ran in kernel mode. A bug in a display driver would crash the entire system (BSOD). Starting with Windows 10 Anniversary Update (1607), Microsoft introduced the Indirect Display Driver model, which runs in User Mode.

An IDD consists of two main parts:
1. **The Driver (UMDF)**: A user-mode DLL that interfaces with the IddCx (Indirect Display Driver Class Extension). It reports monitor arrival/removal and handles frame processing.
2. **The Device**: A software device created by the PnP (Plug and Play) manager, usually installed via an INF file.

When an IDD is installed in Windows Sandbox:
1. It registers a virtual monitor with the OS.
2. The OS (DWM) sees this as a valid rendering target.
3. The OS composes the desktop for this monitor and sends the frames to the IDD.
4. The IDD can simply discard the frames (if pure simulation is needed) or stream them (as used by software like Parsec or Sunshine).

For automation purposes, the mere presence of this active rendering pipeline is sufficient to keep the UI stack alive, even if the user isn't looking at it.

### 3.2 Open Source Virtual Display Implementations

#### 3.2.1 The "Virtual-Display-Driver" (VDD)

Hosted on GitHub (repositories often associated with itsmikethetech, peacepenguin, or VirtualDrivers), this project is the de facto standard for adding virtual monitors to Windows 10 and 11.

**Key Capabilities**:
- **High Resolution Support**: Can simulate resolutions up to 8K (7680x4320). Critical for testing high-DPI scaling scenarios.
- **High Refresh Rates**: Supports up to 240Hz or even 500Hz. Ensures DWM composes frames frequently, reducing input latency.
- **HDR Support**: Newer versions (Windows 11 22H2+) support 10-bit HDR injection.
- **Floating Point Refresh Rates**: Essential for NTSC compatibility testing (e.g., 23.976 fps).

#### 3.2.2 The "IddSampleDriver"

This is Microsoft's reference implementation. While functional, it typically requires compilation and signing by the user. The community VDDs are essentially production-ready forks of this sample, often including signed binaries or instructions for test-signing.

### 3.3 Driver Signing and Installation in Sandbox

A major hurdle in Windows Sandbox is driver signing enforcement. Windows 10/11 (64-bit) requires kernel-mode drivers to be signed by Microsoft (WHQL). However, UMDF drivers (like IDD) have slightly more relaxed requirements but still need a valid signature to be installed via PnP without user intervention.

Most open-source VDDs are self-signed. In a standard Windows environment, installing them requires booting into "Test Mode" or disabling driver signature enforcement.

**Sandbox Limitation**: You cannot easily reboot the Sandbox into "Disable Driver Signature Enforcement" mode because the reboot wipes the state.

**The Solution**: You must add the driver's self-signed certificate to the Trusted Root Certification Authorities store within the Sandbox before attempting installation.

**Installation Logic Flow**:
1. Map a folder containing `driver.inf`, `driver.dll`, and `driver.cer` to the Sandbox.
2. Run a script to import `driver.cer` into `Cert:\LocalMachine\Root`.
3. Use `pnputil /add-driver driver.inf /install` to trigger the PnP installation.
4. Optionally, use a helper executable (`deviceinstaller.exe` or `devcon.exe`) to force the creation of the software device node if pnputil only stages the driver.

### 3.4 Table: Comparison of Display Drivers for Automation

| Feature | Standard RDP Adapter | Indirect Display Driver (IDD) | Hyper-V Video (Synthetic) |
|---------|---------------------|------------------------------|---------------------------|
| Availability | Dependent on RDP Session | Always Available (once installed) | Always Available |
| Rendering State | Suspends on Minimize | Active regardless of host state | Active |
| Resolution Control | Negotiated with Host Client | User-defined (EDID emulation) | Fixed or Negotiated |
| Max Resolution | Host Monitor Resolution | Up to 8K | Limited by vRAM |
| HDR Support | Passthrough (Limited) | Yes (Virtual Injection) | No |
| Installation | Built-in | Requires Script Injection | Built-in (Full VM only) |
| **Suitability for Background** | **Poor** | **Excellent** | Good |

---

## 4. Host-Side Configuration: Overriding RDP Suspension

Installing the VDD ensures the guest has a monitor to render to. However, to ensure the host maintains the connection bandwidth and CPU priority for the Sandbox process, a specific registry modification is required on the host machine.

### 4.1 The RemoteDesktop_SuppressWhenMinimized Registry Key

This registry key is the "magic switch" for background automation involving RDP. It instructs the Terminal Services client (which Windows Sandbox uses) to continue processing the session even when the window is minimized.

**Technical Specification**:
- **Key Path**: `HKEY_LOCAL_MACHINE\Software\Microsoft\Terminal Server Client` OR `HKEY_CURRENT_USER\Software\Microsoft\Terminal Server Client`
- **Value Name**: `RemoteDesktop_SuppressWhenMinimized`
- **Data Type**: `REG_DWORD`
- **Value**: `2`

**Mechanism**: When set to `2`, the client disables the specific PDU transmission that notifies the server of minimization. The server remains unaware that the client is hidden and continues to stream graphical updates. This consumes host bandwidth and CPU, but guarantees that the session remains "active" from the perspective of the guest OS.

### 4.2 Impact on Modern Standby and Power Management

Modern Windows devices often use "Modern Standby" (S0 Low Power Idle). When the screen turns off or the device idles, Windows aggressively suspends background processes.

**Interaction with Sandbox**: Even with the registry key, if the host machine enters a sleep state, the Sandbox (running as a containerized process) will be suspended.

**Mitigation**: For reliable long-running automation, the host must be configured to never sleep (`powercfg /change standby-timeout-ac 0`) while tests are running. The registry key handles the window minimization, but power settings handle the system state.

### 4.3 Side Effects and Scope

This registry change affects **all RDP connections** initiated from the host machine, not just Windows Sandbox. IT administrators using the same machine to manage servers will find that their RDP sessions continue to consume bandwidth when minimized. This is generally an acceptable trade-off for a dedicated automation node or developer workstation.

---

## 5. Automation Strategies: Input Injection and Scripting

With the display pipeline secured via VDD and the session link secured via Registry, the final component is the automation agent itself.

### 5.1 The Input Stack and RDP Limitations

Windows processes input through a complex stack: Hardware Abstraction Layer (HAL) -> Kernel Mode Driver -> Raw Input -> User Mode Message Queue.

**The RDP Problem**: In a standard RDP session, input is virtualized. When the session is minimized (without the registry fix), the virtual keyboard and mouse drivers are effectively detached. Standard API calls like `SendInput` (used by most automation tools) may fail because there is no active desktop to receive the event.

### 5.2 InjectTouchInput: The Robust Alternative

**Research indicates that the Touch Injection API (`InjectTouchInput`) is significantly more robust than mouse injection for background automation.**

**Why?** Touch input is handled by a different subsystem (Windows Pointer Device Stack). It is designed to support multitouch overlays and often bypasses the cursor-position checks that cause mouse events to fail when a screen is locked or headless.

**Implementation**: The API allows the simulation of "hover", "down", "update", and "up" events with precise coordinates. Unlike a mouse cursor, which must "travel" to a location, touch events can be instantly instantiated at x,y coordinates.

**Sample Logic for Touch Injection**:
1. Initialize Touch Injection (`InitializeTouchInjection`).
2. Define a `POINTER_TOUCH_INFO` structure with the target coordinates.
3. Set flag `POINTER_FLAG_DOWN` and call `InjectTouchInput`.
4. Set flag `POINTER_FLAG_UP` and call `InjectTouchInput`.

This sequence simulates a "tap" and is recognized by standard UI controls (WPF, WinForms, UWP) as a click event.

### 5.3 Comparison of Automation Frameworks

| Framework | Mechanism | Background Viability | VDD Requirement |
|-----------|-----------|---------------------|-----------------|
| Selenium (Web) | WebDriver (Browser Protocol) | High | Low (can run headless mode in browser) |
| PyAutoGUI | Image Recognition / Coordinate Click | Zero (fails without active display) | Mandatory (needs VDD to see images) |
| PyWinAuto | UI Automation (UIA) Handles | Medium (can interact with handles) | Recommended (for reliability) |
| AutoIt | Win32 API / Control IDs | Medium | Recommended |
| UiPath | Hybrid (Simulate / Hardware Events) | High (Simulate), Low (Hardware) | Mandatory for Hardware Events |

For "Hardware Event" simulation (which is required for games, legacy apps, or anti-bot protected interfaces), the VDD + Registry Fix combination is the only way to achieve reliability in Sandbox.

---

## 6. Implementation Roadmap: Configuring the Ultimate Sandbox

### 6.1 Step 1: Host Preparation

Before launching the Sandbox, the host must be prepped. This is a one-time configuration (or applied via GPO).

```powershell
# PowerShell script to run on HOST (Administrator)
$registryPath = "HKLM:\Software\Microsoft\Terminal Server Client"
$name = "RemoteDesktop_SuppressWhenMinimized"
$value = 2

if (!(Test-Path $registryPath)) {
    New-Item -Path $registryPath -Force | Out-Null
}
New-ItemProperty -Path $registryPath -Name $name -Value $value -PropertyType DWORD -Force
Write-Host "RDP Minimized Suppression Enabled. Host is ready."
```

### 6.2 Step 2: The Asset Payload

Create a folder on the host (e.g., `C:\SandboxAssets`) containing:
- **Virtual Display Driver Files**: `vdd_driver.inf`, `vdd_driver.dll`, `vdd_driver.cer` (extracted from the open-source release).
- **Automation Script**: `Run-Automation.ps1`.
- **Application Installers**: Any software needed for the test.

### 6.3 Step 3: The Windows Sandbox Configuration (.wsb)

```xml
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>C:\SandboxAssets</HostFolder>
      <SandboxFolder>C:\Assets</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>C:\SandboxOutput</HostFolder>
      <SandboxFolder>C:\Output</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -ExecutionPolicy Bypass -File C:\Assets\Run-Automation.ps1</Command>
  </LogonCommand>
</Configuration>
```

### 6.4 Step 4: The Bootstrapper Script (Run-Automation.ps1)

```powershell
# Run-Automation.ps1 - Executed inside Windows Sandbox

# 1. Trust the Driver Certificate
Write-Host "Importing Driver Certificate..."
Import-Certificate -FilePath "C:\Assets\vdd_driver.cer" -CertStoreLocation Cert:\LocalMachine\Root

# 2. Install the Virtual Display Driver
Write-Host "Installing Virtual Display Driver..."
pnputil /add-driver "C:\Assets\vdd_driver.inf" /install

# 3. Wait for Driver Initialization
Start-Sleep -Seconds 5

# 4. Verify Display Count (Optional Diagnostic)
$monitors = Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBasicDisplayParams
Write-Host "Detected Monitors: $($monitors.Count)"

# 5. Launch Target Application (e.g., Browser or App)
Start-Process "notepad.exe"

# 6. Execute Automation Logic (Example: Python script)
# Start-Process "python" -ArgumentList "C:\Assets\test_script.py" -RedirectStandardOutput "C:\Output\log.txt"
```

### 6.5 Handling "Soft Restarts"

If a specific VDD or application requires a reboot, Windows Sandbox (Build 22621+) supports this. The bootstrapper script can effectively split into two phases:
- **Phase 1**: Install drivers -> Set a "Phase2" marker file in a mapped folder -> `shutdown /r /t 0`.
- **Phase 2**: Upon reboot, the LogonCommand runs again. The script checks for the marker file. If found, it skips installation and proceeds to automation.

Note: This is generally not required for UMDF Indirect Display Drivers.

---

## 7. Security Architecture and Risk Mitigation

### 7.1 Driver Trust Risks

By importing a self-signed certificate into the Sandbox's Trusted Root store, you are technically lowering the security posture of that specific session. If the VDD binary were compromised (e.g., a supply chain attack on the GitHub repo), it would run with user privileges but facilitate input injection.

**Mitigation**: Verify the hash of the VDD binaries before placing them in the `C:\SandboxAssets` folder. Do not download drivers dynamically inside the Sandbox from the internet.

### 7.2 Mapped Folder Vulnerabilities

Mapping `C:\Output` as `ReadOnly=false` creates a channel from the untrusted guest to the host. Malware running in the Sandbox could theoretically write malicious files to this folder.

**Mitigation**: The host should treat `C:\SandboxOutput` as a quarantine zone. Do not execute files from this folder on the host. Use it strictly for text logs (`.txt`) and screenshots (`.png`).

### 7.3 Network Isolation

If the automation does not require internet access (e.g., testing a local build of an app), strictly disable networking in the .wsb file (`<Networking>Disable</Networking>`). This prevents any malware triggered by the automation from beaconing out or moving laterally, while the mapped folders still allow the necessary data ingest/egress.

---

## 8. Troubleshooting and Diagnostics

### 8.1 The "Black Screen" Phenomenon

If screenshots taken by the automation script are purely black:
- **Cause 1**: The RDP `SuppressWhenMinimized` registry key is missing or incorrect on the host.
- **Cause 2**: The DWM in the guest has crashed.
- **Cause 3**: The VDD failed to install.

**Diagnostic**: Check `C:\Output` for the pnputil logs. If the driver installed correctly, the issue is likely the Host Registry.

### 8.2 Common Error Codes

- **0x80070020 (Sharing Violation)**: Often occurs when attempting to map a folder that is locked by another process or when port conflicts occur during kernel debugging.
- **0x5 (Access Denied)**: Occurs if the bootstrapper script tries to write to a `ReadOnly=true` mapped folder. Ensure log paths are directed to the writeable mount.

### 8.3 Kernel Debugging via CmDiag

For advanced driver development or debugging deep crashes, Windows Sandbox supports container debugging.

Command: `CmDiag DevelopmentMode -On` followed by `CmDiag Debug -On -Net`.

This allows a kernel debugger (WinDbg) on the host to attach to the Sandbox kernel via a local network socket, enabling the diagnosis of driver loading failures or DWM crashes.

---

## 9. Conclusion

The transformation of Windows Sandbox into a robust, background-capable automation environment is not achieved through a single setting but through the architectural integration of three distinct layers:

1. **Display Abstraction**: Using Indirect Display Drivers (IDD) to free the guest OS from its dependency on the physical or RDP-negotiated monitor.

2. **Session Persistence**: Leveraging the `RemoteDesktop_SuppressWhenMinimized` host registry key to defeat legacy bandwidth optimizations that throttle background sessions.

3. **Input Synthesis**: Utilizing low-level APIs like `InjectTouchInput` or UI Automation handles to interact with the interface reliably when traditional mouse emulation fails.

This architecture enables enterprise-grade workflows—from high-scale malware detonation to CI/CD UI testing—within the secure, ephemeral, and lightweight constraints of Windows Sandbox. By adhering to the configuration roadmaps and security practices detailed in this report, organizations can unlock the full potential of "ephemeral virtualization" without sacrificing the utility of their physical workstations. The "headless" Sandbox is no longer a contradiction in terms, but a fully realized technical capability.

---

## 10. Summary of Critical Configuration Parameters

| Parameter Category | Setting / Command | Location | Function |
|-------------------|-------------------|----------|----------|
| Host Registry | `RemoteDesktop_SuppressWhenMinimized = 2` | `HKLM\...\Terminal Server Client` | Prevents RDP session suspension on minimize |
| Sandbox Config | `<VGpu>Enable</VGpu>` | .wsb File | Enables GPU acceleration for IDD performance |
| Sandbox Config | `<MappedFolders>` | .wsb File | Bridges VDD installers and scripts to guest |
| Driver Install | `Import-Certificate...` | Guest Script | Trusts the self-signed VDD certificate |
| Driver Install | `pnputil /add-driver... /install` | Guest Script | Installs the IDD into the driver store |
| Input API | `InjectTouchInput` / `POINTER_FLAG_DOWN` | Automation Code | Injects reliable clicks in headless state |

---

## Implications for MCP Tool Coverage Spec

### Validates Our Design Choices:
- ✅ `InjectTouchInput` is the RIGHT choice for touch/pen tools (more reliable than mouse in background)
- ✅ MCP inside sandbox architecture is correct
- ✅ LogonCommand bootstrap approach is correct

### New Requirements to Add:
1. **Host Prerequisites**: One-time registry configuration for `RemoteDesktop_SuppressWhenMinimized`
2. **VDD Installation**: Add to sandbox bootstrap sequence before MCP server starts
3. **VDD Selection**: Need to choose/bundle an open-source IDD (e.g., Virtual-Display-Driver)

### New Tasks:
- Phase 0.5: VDD integration and testing
- Documentation: Host setup guide for registry change
- Security: VDD binary hash verification
