using System;
using System.Text.Json;

namespace Rhombus.WinFormsMcp.Server.Protocol;

/// <summary>
/// Handles MCP/JSON-RPC 2.0 protocol parsing and formatting.
/// </summary>
public class McpProtocol
{
    private int _nextId = 1;

    /// <summary>
    /// Parse a JSON-RPC request from a line of text.
    /// </summary>
    public JsonElement ParseRequest(string line)
    {
        return JsonDocument.Parse(line).RootElement;
    }

    /// <summary>
    /// Extract request id as object to preserve type (JSON-RPC 2.0/MCP allows string or number, not null).
    /// </summary>
    public object GetRequestId(JsonElement request)
    {
        if (request.TryGetProperty("id", out var id))
        {
            return id.ValueKind switch
            {
                JsonValueKind.Number => id.TryGetInt64(out var l) ? l : id.GetDouble(),
                JsonValueKind.String => id.GetString()!,
                JsonValueKind.Null => _nextId++,
                _ => _nextId++
            };
        }
        return _nextId++;
    }

    /// <summary>
    /// Check if a request is a notification (no response expected).
    /// </summary>
    public bool IsNotification(JsonElement request)
    {
        if (!request.TryGetProperty("method", out var methodElement))
            return false;

        var method = methodElement.GetString();
        return method?.StartsWith("notifications/") == true;
    }

    /// <summary>
    /// Get the method name from a request.
    /// </summary>
    public string? GetMethod(JsonElement request)
    {
        return request.TryGetProperty("method", out var methodElement)
            ? methodElement.GetString()
            : null;
    }

    /// <summary>
    /// Format a successful JSON-RPC response.
    /// </summary>
    public object FormatSuccess(object id, object result)
    {
        return new
        {
            jsonrpc = Constants.Protocol.JsonRpcVersion,
            id = id,
            result = result
        };
    }

    /// <summary>
    /// Format a JSON-RPC error response.
    /// </summary>
    public object FormatError(object id, int code, string message, object? data = null)
    {
        if (data != null)
        {
            return new
            {
                jsonrpc = Constants.Protocol.JsonRpcVersion,
                id = id,
                error = new
                {
                    code = code,
                    message = message,
                    data = data
                }
            };
        }

        return new
        {
            jsonrpc = Constants.Protocol.JsonRpcVersion,
            id = id,
            error = new
            {
                code = code,
                message = message
            }
        };
    }

    /// <summary>
    /// Format an internal error response.
    /// </summary>
    public object FormatInternalError(object id, string details)
    {
        return FormatError(id, Constants.JsonRpcErrors.InternalError, "Internal error", new { details });
    }
}
