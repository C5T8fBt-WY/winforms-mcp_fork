using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace C5T8fBtWY.WinFormsMcp.Server.Handlers;

/// <summary>
/// Interface for tool handlers that process MCP tool calls.
/// Each handler is responsible for a group of related tools.
/// </summary>
internal interface IToolHandler
{
    /// <summary>
    /// The tool names this handler supports.
    /// </summary>
    IEnumerable<string> SupportedTools { get; }

    /// <summary>
    /// Execute a tool with the given arguments.
    /// </summary>
    /// <param name="toolName">The name of the tool to execute.</param>
    /// <param name="args">The JSON arguments for the tool.</param>
    /// <returns>The result as a JsonElement.</returns>
    Task<JsonElement> ExecuteAsync(string toolName, JsonElement args);
}
