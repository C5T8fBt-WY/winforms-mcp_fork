using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Sandbox;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Handles sandbox tools: launch_app_sandboxed, close_sandbox, list_sandbox_apps.
/// </summary>
internal class SandboxHandlers : HandlerBase
{
    public SandboxHandlers(SessionManager session, WindowManager windows)
        : base(session, windows)
    {
    }

    public override IEnumerable<string> SupportedTools => new[]
    {
        "launch_app_sandboxed",
        "close_sandbox",
        "list_sandbox_apps"
    };

    public override Task<JsonElement> ExecuteAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "launch_app_sandboxed" => LaunchAppSandboxed(args),
            "close_sandbox" => CloseSandbox(args),
            "list_sandbox_apps" => ListSandboxApps(args),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }

    private async Task<JsonElement> LaunchAppSandboxed(JsonElement args)
    {
        try
        {
            var appPath = GetStringArg(args, "appPath") ?? throw new ArgumentException("appPath is required");
            var appExe = GetStringArg(args, "appExe") ?? throw new ArgumentException("appExe is required");
            var mcpServerPath = GetStringArg(args, "mcpServerPath") ?? throw new ArgumentException("mcpServerPath is required");
            var sharedFolderPath = GetStringArg(args, "sharedFolderPath") ?? throw new ArgumentException("sharedFolderPath is required");
            var outputFolderPath = GetStringArg(args, "outputFolderPath");
            var bootTimeoutMs = GetIntArg(args, "bootTimeoutMs", 60000);

            var sandboxManager = Session.GetSandboxManager();

            // Check if sandbox is already running
            if (sandboxManager.IsRunning)
            {
                return ToolResponse.Fail("Sandbox is already running. Call close_sandbox first.", Windows).ToJsonElement();
            }

            // Ensure shared folder exists
            if (!Directory.Exists(sharedFolderPath))
            {
                Directory.CreateDirectory(sharedFolderPath);
            }

            // Build the .wsb configuration
            var builder = SandboxConfigurations.CreateMcpSandbox(
                appPath,
                mcpServerPath,
                sharedFolderPath,
                outputFolderPath);

            // Generate a temp .wsb file
            var wsbPath = Path.Combine(Path.GetTempPath(), $"mcp-sandbox-{Guid.NewGuid():N}.wsb");
            builder.BuildAndSave(wsbPath);

            // Set boot timeout
            sandboxManager.BootTimeoutMs = bootTimeoutMs;

            // Launch the sandbox
            var result = await sandboxManager.LaunchSandboxAsync(wsbPath, sharedFolderPath);

            // Clean up the .wsb file (sandbox has already read it)
            try { File.Delete(wsbPath); } catch { }

            if (result.Success)
            {
                return ToolResponse.Ok(Windows,
                    ("message", "Sandbox launched and MCP server ready"),
                    ("processId", result.ProcessId),
                    ("sharedFolderPath", result.SharedFolderPath ?? "")).ToJsonElement();
            }
            else
            {
                return ToolResponse.Fail(result.Error ?? "Unknown error", Windows,
                    ("sandboxAvailable", result.SandboxAvailable)).ToJsonElement();
            }
        }
        catch (ArgumentException ex)
        {
            // Security validation errors from WsbConfigBuilder
            return ToolResponse.Fail(ex.Message, Windows).ToJsonElement();
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, Windows).ToJsonElement();
        }
    }

    private async Task<JsonElement> CloseSandbox(JsonElement args)
    {
        try
        {
            var timeoutMs = GetIntArg(args, "timeoutMs", Constants.Timeouts.SandboxShutdown);

            var sandboxManager = Session.GetSandboxManager();

            if (!sandboxManager.IsRunning)
            {
                return ToolResponse.Ok(Windows, ("message", "No sandbox was running")).ToJsonElement();
            }

            await sandboxManager.CloseSandboxAsync(timeoutMs);

            return ToolResponse.Ok(Windows, ("message", "Sandbox closed successfully")).ToJsonElement();
        }
        catch (Exception ex)
        {
            return ToolResponse.Fail(ex.Message, Windows).ToJsonElement();
        }
    }

    private Task<JsonElement> ListSandboxApps(JsonElement args)
    {
        try
        {
            var appFolder = GetStringArg(args, "appFolder") ?? @"C:\App";

            if (!Directory.Exists(appFolder))
            {
                return Error($"App folder not found: {appFolder}");
            }

            var apps = new List<object>();

            // Scan for executables and their associated DLLs
            var exeFiles = Directory.GetFiles(appFolder, "*.exe", SearchOption.AllDirectories);
            var dllFiles = Directory.GetFiles(appFolder, "*.dll", SearchOption.AllDirectories);

            foreach (var exe in exeFiles)
            {
                var relativePath = Path.GetRelativePath(appFolder, exe);
                var fileName = Path.GetFileNameWithoutExtension(exe);

                // Check if there's a matching .dll (for dotnet.exe launch)
                var matchingDll = dllFiles.FirstOrDefault(d =>
                    Path.GetFileNameWithoutExtension(d).Equals(fileName, StringComparison.OrdinalIgnoreCase));

                apps.Add(new
                {
                    name = fileName,
                    exe_path = exe,
                    relative_path = relativePath,
                    dll_path = matchingDll,
                    can_launch_with_dotnet = matchingDll != null
                });
            }

            return Success(
                ("app_folder", appFolder),
                ("apps", apps),
                ("count", apps.Count));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }
}
