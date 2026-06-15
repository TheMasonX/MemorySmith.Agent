namespace Agent.Tools;

using Agent.Core;
using System.Collections.Concurrent;

/// <summary>
/// Thread-safe in-memory registry of MCP tools.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool) => _tools[tool.Name] = tool;
    public ITool? Get(string name) => _tools.TryGetValue(name, out var t) ? t : null;
    public IReadOnlyList<ITool> All => [.. _tools.Values];
}
