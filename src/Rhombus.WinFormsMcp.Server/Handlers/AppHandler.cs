using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Interop;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Unified handler for application lifecycle: launch, attach, close, info.
/// Replaces: launch_app, attach_to_process, close_app, get_process_info
/// </summary>
internal class AppHandler : HandlerBase
{
    public AppHandler(ISessionManager session, IWindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[] { "app" };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        var action = GetStringArg(args, "action");

        return action switch
        {
            "launch" => Launch(args),
            "attach" => Attach(args),
            "close" => Close(args),
            "info" => Info(args),
            _ => Error($"Unknown action: {action}. Expected: launch, attach, close, info")
        };
    }

    private Task<JsonElement> Launch(JsonElement args)
    {
        try
        {
            var path = GetStringArg(args, "path");
            if (string.IsNullOrEmpty(path))
                return Error("path is required. Example: {\"action\": \"launch\", \"path\": \"C:\\\\App\\\\MyApp.exe\"}");
            var arguments = GetStringArg(args, "args");
            var workingDir = GetStringArg(args, "working_directory") ?? Path.GetDirectoryName(path);
            var waitMs = GetIntArg(args, "wait_ms", Constants.Timeouts.AppIdle);

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
                    // Process already exited
                }
                catch
                {
                    // Ignore errors closing previous instance
                }
            }

            var process = automation.LaunchApp(path, arguments, workingDir, waitMs);

            Session.CacheProcess(process.Id, process);
            Session.TrackLaunchedApp(path, process.Id);
            Session.TrackProcess(process.Id);

            // Write state file for hot-reload
            try
            {
                var appName = Path.GetFileNameWithoutExtension(path);
                var stateFile = @"C:\Shared\current_app.state";
                if (Directory.Exists(@"C:\Shared"))
                {
                    File.WriteAllText(stateFile, $"{appName}\n{process.Id}");
                }
            }
            catch { /* Non-critical */ }

            var result = new Dictionary<string, object?>
            {
                ["pid"] = process.Id,
                ["processName"] = process.ProcessName
            };

            if (previousPid.HasValue)
            {
                result["previousPid"] = previousPid.Value;
                result["previousClosed"] = previousClosed;
            }

            var scopedWindows = Windows.GetWindowsByPid(process.Id);
            return Task.FromResult(ToolResponse.OkScoped(result, WindowScope.Process, scopedWindows).ToJsonElement());
        }
        catch (System.IO.FileNotFoundException)
        {
            return Error($"Executable not found at path. Verify the path exists and is accessible.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            return Error($"Access denied - application may require elevated permissions.");
        }
        catch (TimeoutException)
        {
            return Error($"Application launched but no window appeared within timeout. The app may have crashed or is running headless.");
        }
        catch (Exception ex)
        {
            return Error($"Launch failed: {ex.Message}");
        }
    }

    private Task<JsonElement> Attach(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            var title = GetStringArg(args, "title");

            var automation = Session.GetAutomation();
            Process process;

            if (!string.IsNullOrEmpty(title))
            {
                // Find window by title, then get its process
                var window = automation.GetWindowByTitle(title);
                if (window == null)
                    return Error($"Window not found: {title}");

                var windowPid = window.Properties.ProcessId.ValueOrDefault;
                process = Process.GetProcessById(windowPid);
            }
            else if (pid > 0)
            {
                process = automation.AttachToProcess(pid);
            }
            else
            {
                return Error("Either pid or title is required for attach");
            }

            Session.CacheProcess(process.Id, process);
            Session.TrackProcess(process.Id);

            var scopedWindows = Windows.GetWindowsByPid(process.Id);
            return Task.FromResult(ToolResponse.OkScoped(
                new { pid = process.Id, processName = process.ProcessName },
                WindowScope.Process,
                scopedWindows).ToJsonElement());
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Process"))
        {
            return Error("Process not found. It may have exited. Use find with at:'root' to discover running windows.");
        }
        catch (Exception ex)
        {
            return Error($"Attach failed: {ex.Message}");
        }
    }

    private Task<JsonElement> Close(JsonElement args)
    {
        try
        {
            // Direct HWND close: closes a modal dialog or any window without needing a PID boundary.
            // Use this when a ShowDialog-based form has no PID mapping (e.g. config panels).
            var handle = GetStringArg(args, "handle");
            if (handle != null && handle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var hwnd = new IntPtr(Convert.ToInt64(handle, 16));
                WindowInterop.PostMessage(hwnd, WindowInterop.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return ScopedSuccess(args, new { closed = true, hwnd = handle });
            }

            var pid = GetIntArg(args, "pid");
            if (pid <= 0)
                return Error("Either handle (\"0xHWND\") or pid is required for close");

            var automation = Session.GetAutomation();
            automation.CloseApp(pid, false, Constants.Timeouts.CloseApp);
            Session.UntrackProcess(pid);

            var trackedPids = Session.GetTrackedProcessIds();
            var scopedWindows = trackedPids.Count > 0
                ? Windows.GetWindowsByPids(trackedPids)
                : Windows.GetAllWindows();
            var scope = trackedPids.Count > 0 ? WindowScope.Tracked : WindowScope.All;

            return Task.FromResult(ToolResponse.OkScoped(
                new { closed = true },
                scope,
                scopedWindows).ToJsonElement());
        }
        catch (ArgumentException)
        {
            return Error("Process not found. It may have already exited.");
        }
        catch (Exception ex)
        {
            return Error($"Close failed: {ex.Message}");
        }
    }

    private Task<JsonElement> Info(JsonElement args)
    {
        try
        {
            var pid = GetIntArg(args, "pid");
            if (pid <= 0)
                return Error("pid is required for info");

            var process = Process.GetProcessById(pid);

            return Success(new
            {
                pid = process.Id,
                processName = process.ProcessName,
                responding = process.Responding,
                mainWindowTitle = process.MainWindowTitle,
                mainWindowHandle = $"0x{process.MainWindowHandle.ToInt64():X8}"
            });
        }
        catch (ArgumentException)
        {
            return Error("Process not found. It may have exited.");
        }
        catch (Exception ex)
        {
            return Error($"Info failed: {ex.Message}");
        }
    }
}
