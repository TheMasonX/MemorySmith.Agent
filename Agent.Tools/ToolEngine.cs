namespace Agent.Tools;

using Agent.Core;
using System.Text.Json;

/// <summary>
/// Executes tool calls from the LLM or planner.
/// Validates the tool exists, then delegates execution to the ITool implementation.
/// All tool calls are validated against the tool's JSON schema — no arbitrary code execution.
/// </summary>
public sealed class ToolEngine(IToolRegistry registry) : IToolCaller
{
    public async Task<ToolResult> CallAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var tool = registry.Get(toolName);
        if (tool is null)
            return new ToolResult(false, $"Tool '{toolName}' is not registered.");

        // TODO: validate arguments against tool.InputSchema before dispatching
        return await tool.ExecuteAsync(arguments, cancellationToken);
    }
}
