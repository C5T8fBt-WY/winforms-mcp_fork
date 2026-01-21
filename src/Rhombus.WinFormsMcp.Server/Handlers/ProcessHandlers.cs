using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles process lifecycle tools: launch_app, attach_to_process, close_app, get_process_info.
/// </summary>
internal class ProcessHandlers : HandlerBase
{
    public ProcessHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "launch_app",
        "attach_to_process",
        "close_app",
        "get_process_info"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "launch_app" => LaunchApp(args),
            "attach_to_process" => AttachToProcess(args),
            "close_app" => CloseApp(args),
            "get_process_info" => GetProcessInfo(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private Task<JsonElement> LaunchApp(JsonElement args)
    {
        try
        {
            var path = GetStringArg(args, "path") ?? throw new ArgumentException("path is required");
            var arguments = GetStringArg(args, "arguments");
            var workingDirectory = GetStringArg(args, "workingDirectory");
            var idleTimeoutMs = GetIntArg(args, "idleTimeoutMs", Constants.Timeouts.AppIdle);

            var automation = Session.GetAutomation();

            // Check if there's a previous instance of this app running and close it
            int? previousPid = null;
            bool previousClosed = false;
            var prevPid = Session.GetPreviousLaunchedPid(path);
            if (prevPid.HasValue)
            {
                previousPid = prevPid.Value;
                try
                {
                    var prevProcess = Process.GetProcessById(prevPid.Value);
                    if (!prevProcess.HasExited)
                    {
                        prevProcess.CloseMainWindow();
                        if (!prevProcess.WaitForExit(2000))
                        {
                            prevProcess.Kill();
                            prevProcess.WaitForExit(Constants.Timeouts.ProcessExitWait);
                        }
                        previousClosed = true;
                    }
                }
                catch (ArgumentException)
                {
                    // Process already exited, that's fine
                }
                catch (Exception)
                {
                    // Ignore errors closing previous instance
                }
            }

            var process = automation.LaunchApp(path, arguments, workingDirectory, idleTimeoutMs);

            Session.CacheProcess(process.Id, process);
            Session.TrackLaunchedApp(path, process.Id);
            Session.TrackProcess(process.Id); // Track for window scoping

            // Write current app name and PID to state file for bootstrap hot-reload
            try
            {
                var appName = Path.GetFileNameWithoutExtension(path);
                var stateFile = @"C:\Shared\current_app.state";
                if (Directory.Exists(@"C:\Shared"))
                {
                    // Format: AppName\nPID (bootstrap reads both for hot-reload)
                    File.WriteAllText(stateFile, $"{appName}\n{process.Id}");
                }
            }
            catch
            {
                // Ignore errors writing state file - not critical
            }

            var props = new List<(string, object?)>
            {
                ("pid", process.Id),
                ("processName", process.ProcessName)
            };

            if (previousPid.HasValue)
            {
                props.Add(("previousPid", previousPid.Value));
                props.Add(("previousClosed", previousClosed));
            }

            // Return scoped windows for the newly launched process
            var scopedWindows = Windows.GetWindowsByPid(process.Id);
            return Task.FromResult(ToolResponse.OkScoped(WindowScope.Process, scopedWindows, props.ToArray()).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> AttachToProcess(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var processName = GetStringArg(args, "processName");

            var automation = Session.GetAutomation();
            var process = !string.IsNullOrEmpty(processName)
                ? automation.AttachToProcessByName(processName)
                : automation.AttachToProcess(pid);

            Session.CacheProcess(process.Id, process);
            Session.TrackProcess(process.Id); // Track for window scoping

            // Return scoped windows for the attached process
            var scopedWindows = Windows.GetWindowsByPid(process.Id);
            return Task.FromResult(ToolResponse.OkScoped(
                new { pid = process.Id, processName = process.ProcessName },
                WindowScope.Process,
                scopedWindows).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> CloseApp(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var force = GetBoolArg(args, "force", false);
            var closeTimeoutMs = GetIntArg(args, "closeTimeoutMs", Constants.Timeouts.CloseApp);

            var automation = Session.GetAutomation();
            automation.CloseApp(pid, force, closeTimeoutMs);
            Session.UntrackProcess(pid); // Stop tracking for window scoping

            // Return scoped windows (tracked processes only)
            var trackedPids = Session.GetTrackedProcessIds();
            var scopedWindows = Windows.GetWindowsByPids(trackedPids);
            var scope = trackedPids.Count > 0 ? WindowScope.Tracked : WindowScope.All;
            if (scope == WindowScope.All)
                scopedWindows = Windows.GetAllWindows();

            return Task.FromResult(ToolResponse.OkScoped(
                new { message = "Application closed" },
                scope,
                scopedWindows).ToJsonElement());
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private Task<JsonElement> GetProcessInfo(JsonElement args)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            IntPtr hwnd = IntPtr.Zero;
            var windowHandle = GetStringArg(args, "windowHandle");
            var windowTitle = GetStringArg(args, "windowTitle");

            if (!string.IsNullOrEmpty(windowHandle))
            {
                // Parse hex or decimal HWND
                if (windowHandle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    hwnd = new IntPtr(Convert.ToInt64(windowHandle, 16));
                }
                else
                {
                    hwnd = new IntPtr(long.Parse(windowHandle));
                }
            }
            else if (!string.IsNullOrEmpty(windowTitle))
            {
                // Find window by title using InputInjection helper
                var bounds = InputInjection.GetWindowBounds(windowTitle);
                if (bounds == null)
                {
                    stopwatch.Stop();
                    return Error($"Window not found: {windowTitle}");
                }

                // We need to find the HWND - use FindWindow
                hwnd = FindWindowByTitle(windowTitle);
                if (hwnd == IntPtr.Zero)
                {
                    stopwatch.Stop();
                    return Error($"Could not get window handle for: {windowTitle}");
                }
            }
            else
            {
                stopwatch.Stop();
                return Error("Either windowHandle or windowTitle is required");
            }

            // Validate window handle
            if (!IsWindow(hwnd))
            {
                stopwatch.Stop();
                return Error("Invalid window handle");
            }

            // Get process ID
            GetWindowThreadProcessId(hwnd, out uint pid);

            // Get process info
            string processName = "Unknown";
            bool isResponding = false;
            try
            {
                var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName + ".exe";
                isResponding = process.Responding;
            }
            catch { /* Process may have exited */ }

            // Get window state
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(hwnd, ref placement);

            string windowState = placement.showCmd switch
            {
                SW_MINIMIZED => "minimized",
                SW_MAXIMIZED => "maximized",
                _ => "normal"
            };

            stopwatch.Stop();

            return Success(
                ("pid", (int)pid),
                ("process_name", processName),
                ("is_responding", isResponding),
                ("window_state", windowState),
                ("main_window_handle", $"0x{hwnd.ToInt64():X8}"),
                ("execution_time_ms", stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    #region Win32 Interop

    private const int SW_MINIMIZED = 2;
    private const int SW_MAXIMIZED = 3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    private delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public System.Drawing.Point ptMinPosition;
        public System.Drawing.Point ptMaxPosition;
        public System.Drawing.Rectangle rcNormalPosition;
    }

    private static IntPtr FindWindowByTitle(string partialTitle)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hwnd, lParam) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (!string.IsNullOrEmpty(title) && title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
            {
                foundHwnd = hwnd;
                return false; // Stop enumeration
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return foundHwnd;
    }

    #endregion
}
