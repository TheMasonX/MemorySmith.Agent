namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Dispatches a named tool call from the LLM. Validates arguments against the
/// registered tool's JSON schema before execution. Prevents unsafe raw execution.
/// </summary>
public interface IToolCaller
{
    Task<ToolResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default);
}
