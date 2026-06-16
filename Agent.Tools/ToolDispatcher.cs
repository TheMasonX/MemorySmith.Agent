namespace Agent.Tools;

using Agent.Core;
using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// Deep module: registers tools, resolves by name, and executes with schema validation.
///
/// Replaces the former ToolRegistry + ToolEngine pair (two shallow modules that the
/// deletion test confirmed earned no separate interface depth). The caller surface shrinks
/// from four types (IToolRegistry, IToolCaller, ToolRegistry, ToolEngine) to two
/// (IToolCaller stays for DI; ToolDispatcher is the single concrete class).
///
/// When the LLM path matures, schema validation lives here — not scattered across callers.
/// </summary>
public sealed class ToolDispatcher : IToolCaller
{
    private readonly ConcurrentDictionary<string, ITool> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Registration ──────────────────────────────────────────────────────────

    public void Register(ITool tool) => _tools[tool.Name] = tool;

    public ITool? Get(string name) =>
        _tools.TryGetValue(name, out var t) ? t : null;

    public IReadOnlyList<ITool> All => [.. _tools.Values];

    // ── Dispatch ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the named tool and executes it.
    /// Returns a failure result (not throws) when the tool is unknown —
    /// the caller's dispatch loop decides whether to retry or abandon.
    /// </summary>
    public async Task<ToolResult> CallAsync(
        string toolName, JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var tool = _tools.TryGetValue(toolName, out var t) ? t : null;
        if (tool is null)
            return new ToolResult(false, $"Tool '{toolName}' is not registered.");

        // TODO: validate arguments against tool.InputSchema before dispatching
        return await tool.ExecuteAsync(arguments, cancellationToken);
    }
}
