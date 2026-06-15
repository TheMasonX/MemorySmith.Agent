namespace Agent.Core;

using System.Text.Json;

/// <summary>
/// A typed tool available to the LLM via Model Context Protocol (MCP).
/// Each tool has a name, description (for prompt context), and a JSON Schema
/// describing its arguments and return shape.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }

    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}
