using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rhombus.WinFormsMcp.Server.Abstractions;
using Rhombus.WinFormsMcp.Server.Models;

namespace Rhombus.WinFormsMcp.Server;

/// <summary>
/// Scope of windows returned in the response.
/// </summary>
public enum WindowScope
{
    /// <summary>
    /// All visible windows on the desktop.
    /// </summary>
    All,

    /// <summary>
    /// Windows from a single process.
    /// </summary>
    Process,

    /// <summary>
    /// Windows from tracked processes only.
    /// </summary>
    Tracked
}

/// <summary>
/// Standardized response format for all MCP tools.
/// Every response includes window context so agents always know available windows.
/// </summary>
public class ToolResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; set; }

    [JsonPropertyName("windows")]
    public List<WindowInfo> Windows { get; set; } = new();

    /// <summary>
    /// Scope of the windows array - indicates if filtered or all windows.
    /// </summary>
    [JsonPropertyName("windowScope")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowScope WindowScope { get; set; } = WindowScope.All;

    // Additional context for specific error types
    [JsonPropertyName("partialMatches")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WindowInfo>? PartialMatches { get; set; }

    [JsonPropertyName("matches")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WindowInfo>? Matches { get; set; }

    /// <summary>
    /// Create a successful response with window context.
    /// </summary>
    public static ToolResponse Ok(object? result, IWindowManager windowManager)
    {
        return new ToolResponse
        {
            Success = true,
            Result = result,
            Windows = windowManager.GetAllWindows()
        };
    }

    /// <summary>
    /// Create a successful response with custom properties merged in.
    /// </summary>
    public static ToolResponse Ok(IWindowManager windowManager, params (string key, object? value)[] properties)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in properties)
        {
            result[key] = value;
        }

        return new ToolResponse
        {
            Success = true,
            Result = result,
            Windows = windowManager.GetAllWindows()
        };
    }

    /// <summary>
    /// Create a failure response with window context.
    /// </summary>
    public static ToolResponse Fail(string error, IWindowManager windowManager)
    {
        return new ToolResponse
        {
            Success = false,
            Error = error,
            Windows = windowManager.GetAllWindows()
        };
    }

    /// <summary>
    /// Create a failure response with custom properties merged in.
    /// </summary>
    public static ToolResponse Fail(string error, IWindowManager windowManager, params (string key, object? value)[] properties)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in properties)
        {
            result[key] = value;
        }

        return new ToolResponse
        {
            Success = false,
            Error = error,
            Result = result.Count > 0 ? result : null,
            Windows = windowManager.GetAllWindows()
        };
    }

    /// <summary>
    /// Create a failure response with partial matches for window-not-found errors.
    /// </summary>
    public static ToolResponse FailWithPartialMatches(string error, List<WindowInfo> partialMatches, IWindowManager windowManager)
    {
        return new ToolResponse
        {
            Success = false,
            Error = error,
            PartialMatches = partialMatches.Count > 0 ? partialMatches : null,
            Windows = windowManager.GetAllWindows()
        };
    }

    /// <summary>
    /// Create a failure response for multiple window matches.
    /// </summary>
    public static ToolResponse FailWithMultipleMatches(string error, List<WindowInfo> matches, IWindowManager windowManager)
    {
        return new ToolResponse
        {
            Success = false,
            Error = error,
            Matches = matches,
            Windows = windowManager.GetAllWindows()
        };
    }

    /// <summary>
    /// Create a successful response with scoped windows.
    /// </summary>
    /// <param name="result">The result payload.</param>
    /// <param name="windowScope">The scope of the windows array.</param>
    /// <param name="windows">The scoped windows list.</param>
    public static ToolResponse OkScoped(object? result, WindowScope windowScope, List<WindowInfo> windows)
    {
        return new ToolResponse
        {
            Success = true,
            Result = result,
            Windows = windows,
            WindowScope = windowScope
        };
    }

    /// <summary>
    /// Create a successful response with scoped windows and custom properties.
    /// </summary>
    public static ToolResponse OkScoped(WindowScope windowScope, List<WindowInfo> windows, params (string key, object? value)[] properties)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in properties)
        {
            result[key] = value;
        }

        return new ToolResponse
        {
            Success = true,
            Result = result.Count > 0 ? result : null,
            Windows = windows,
            WindowScope = windowScope
        };
    }

    /// <summary>
    /// Create a failure response with all windows for error recovery context.
    /// Used when scoping would hide relevant context during errors.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="allWindows">All available windows for recovery context.</param>
    public static ToolResponse FailWithContext(string error, List<WindowInfo> allWindows)
    {
        return new ToolResponse
        {
            Success = false,
            Error = error,
            Windows = allWindows,
            WindowScope = WindowScope.All
        };
    }

    /// <summary>
    /// Create a failure response with all windows and custom properties.
    /// </summary>
    public static ToolResponse FailWithContext(string error, List<WindowInfo> allWindows, params (string key, object? value)[] properties)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in properties)
        {
            result[key] = value;
        }

        return new ToolResponse
        {
            Success = false,
            Error = error,
            Result = result.Count > 0 ? result : null,
            Windows = allWindows,
            WindowScope = WindowScope.All
        };
    }

    /// <summary>
    /// Serialize to JSON string for MCP response.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Serialize to JsonElement for existing tool handler pattern.
    /// </summary>
    public JsonElement ToJsonElement()
    {
        var json = ToJson();
        return JsonDocument.Parse(json).RootElement;
    }
}
