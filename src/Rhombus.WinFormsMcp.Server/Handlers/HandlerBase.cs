using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Models;
using Rhombus.WinFormsMcp.Server.Utilities;

namespace Rhombus.WinFormsMcp.Server.Handlers;

/// <summary>
/// Base class for tool handlers providing common functionality and helper methods.
/// </summary>
internal abstract class HandlerBase : IToolHandler
{
    protected readonly ISessionManager Session;
    protected readonly IWindowManager Windows;

    protected HandlerBase(ISessionManager session, IWindowManager windows)
    {
        Session = session;
        Windows = windows;
    }

    /// <summary>
    /// The tool names this handler supports.
    /// </summary>
    public abstract IEnumerable<string> SupportedTools { get; }

    /// <summary>
    /// Execute a tool with the given arguments.
    /// </summary>
    public abstract Task<JsonElement> ExecuteAsync(string toolName, JsonElement args);

    #region Argument Helpers

    /// <summary>
    /// Get a string argument from JSON args.
    /// Delegates to ArgHelpers for consistent behavior.
    /// </summary>
    protected string? GetStringArg(JsonElement args, string key)
        => ArgHelpers.GetString(args, key);

    /// <summary>
    /// Get an integer argument from JSON args with default value.
    /// Delegates to ArgHelpers for consistent behavior.
    /// </summary>
    protected int GetIntArg(JsonElement args, string key, int defaultValue = 0)
        => ArgHelpers.GetInt(args, key, defaultValue);

    /// <summary>
    /// Get a double argument from JSON args with default value.
    /// Delegates to ArgHelpers for consistent behavior.
    /// </summary>
    protected double GetDoubleArg(JsonElement args, string key, double defaultValue = 0)
        => ArgHelpers.GetDouble(args, key, defaultValue);

    /// <summary>
    /// Get a boolean argument from JSON args with default value.
    /// Delegates to ArgHelpers for consistent behavior.
    /// </summary>
    protected bool GetBoolArg(JsonElement args, string key, bool defaultValue = false)
        => ArgHelpers.GetBool(args, key, defaultValue);

    /// <summary>
    /// Get a uint argument from JSON args with default value.
    /// Delegates to ArgHelpers for consistent behavior.
    /// </summary>
    protected uint GetUIntArg(JsonElement args, string key, uint defaultValue = 0)
        => ArgHelpers.GetUInt(args, key, defaultValue);

    /// <summary>
    /// Get an enum argument from JSON args.
    /// Delegates to ArgHelpers for consistent behavior.
    /// </summary>
    protected T? GetEnumArg<T>(JsonElement args, string key) where T : struct, Enum
        => ArgHelpers.GetEnum<T>(args, key);

    #endregion

    #region Response Helpers

    /// <summary>
    /// Create a successful response.
    /// </summary>
    protected Task<JsonElement> Success(params (string key, object? value)[] properties)
    {
        return Task.FromResult(ToolResponse.Ok(Windows, properties).ToJsonElement());
    }

    /// <summary>
    /// Create a successful response with a result object.
    /// </summary>
    protected Task<JsonElement> Success(object? result)
    {
        return Task.FromResult(ToolResponse.Ok(result, Windows).ToJsonElement());
    }

    /// <summary>
    /// Create a successful response with an optional warning.
    /// </summary>
    protected Task<JsonElement> SuccessWithWarning(string? warning, params (string key, object? value)[] properties)
    {
        var response = ToolResponse.Ok(Windows, properties);
        response.Warning = warning;
        return Task.FromResult(response.ToJsonElement());
    }

    /// <summary>
    /// Create an error response.
    /// </summary>
    protected Task<JsonElement> Error(string message)
    {
        return Task.FromResult(ToolResponse.Fail(message, Windows).ToJsonElement());
    }

    /// <summary>
    /// Create an error response with additional properties.
    /// </summary>
    protected Task<JsonElement> Error(string message, params (string key, object? value)[] properties)
    {
        return Task.FromResult(ToolResponse.Fail(message, Windows, properties).ToJsonElement());
    }

    /// <summary>
    /// Create a scoped success response based on tracked processes.
    /// If includeAllWindows is true, returns all windows regardless of tracking.
    /// </summary>
    protected Task<JsonElement> ScopedSuccess(JsonElement args, params (string key, object? value)[] properties)
    {
        var includeAllWindows = GetBoolArg(args, "includeAllWindows", false);
        var (scope, windows) = GetScopedWindows(includeAllWindows);
        return Task.FromResult(ToolResponse.OkScoped(scope, windows, properties).ToJsonElement());
    }

    /// <summary>
    /// Create a scoped success response with a result object.
    /// </summary>
    protected Task<JsonElement> ScopedSuccess(JsonElement args, object? result)
    {
        var includeAllWindows = GetBoolArg(args, "includeAllWindows", false);
        var (scope, windows) = GetScopedWindows(includeAllWindows);
        return Task.FromResult(ToolResponse.OkScoped(result, scope, windows).ToJsonElement());
    }

    /// <summary>
    /// Get windows scoped to tracked processes, or all if no tracking.
    /// </summary>
    protected (WindowScope scope, List<WindowInfo> windows) GetScopedWindows(bool includeAllWindows)
    {
        if (includeAllWindows)
            return (WindowScope.All, Windows.GetAllWindows());

        var trackedPids = Session.GetTrackedProcessIds();
        if (trackedPids.Count == 0)
            return (WindowScope.All, Windows.GetAllWindows());

        return (WindowScope.Tracked, Windows.GetWindowsByPids(trackedPids));
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Escape a string for JSON serialization.
    /// </summary>
    protected string EscapeJson(string? value)
    {
        if (value == null)
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    /// <summary>
    /// Capture an element's screenshot to base64-encoded PNG.
    /// </summary>
    protected string CaptureElementToBase64(AutomationElement element)
    {
        var bitmap = element.Capture();
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// Capture a screen region to base64-encoded PNG.
    /// </summary>
    protected string CaptureRegionToBase64(int x, int y, int width, int height)
    {
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Capture a window by HWND using PrintWindow API — works even when the window is
    /// behind other windows (no focus stealing, no Z-order changes).
    /// </summary>
    protected string CaptureWindowByHwndToBase64(IntPtr hwnd)
    {
        Interop.WindowInterop.GetWindowRect(hwnd, out var rect);
        int w = rect.Width;
        int h = rect.Height;
        if (w <= 0 || h <= 0)
            throw new InvalidOperationException($"Window 0x{hwnd:X} has no visible area ({w}x{h}).");

        using var bitmap = new System.Drawing.Bitmap(w, h);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                Interop.WindowInterop.PrintWindow(hwnd, hdc, Interop.WindowInterop.PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Capture the full desktop to base64-encoded PNG.
    /// </summary>
    protected string CaptureDesktopToBase64()
    {
        var automation = Session.GetAutomation();
        var desktop = automation.GetDesktop();
        var bitmap = desktop.Capture();
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    #endregion
}
