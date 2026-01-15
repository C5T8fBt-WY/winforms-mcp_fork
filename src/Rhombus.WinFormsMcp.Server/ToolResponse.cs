using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rhombus.WinFormsMcp.Server.Automation;
using Rhombus.WinFormsMcp.Server.Models;

namespace Rhombus.WinFormsMcp.Server;

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

    [JsonPropertyName("windows")]
    public List<WindowInfo> Windows { get; set; } = new();

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
    public static ToolResponse Ok(object? result, WindowManager windowManager)
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
    public static ToolResponse Ok(WindowManager windowManager, params (string key, object? value)[] properties)
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
    public static ToolResponse Fail(string error, WindowManager windowManager)
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
    public static ToolResponse Fail(string error, WindowManager windowManager, params (string key, object? value)[] properties)
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
    public static ToolResponse FailWithPartialMatches(string error, List<WindowInfo> partialMatches, WindowManager windowManager)
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
    public static ToolResponse FailWithMultipleMatches(string error, List<WindowInfo> matches, WindowManager windowManager)
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
