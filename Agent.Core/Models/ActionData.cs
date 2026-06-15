namespace Agent.Core;

/// <summary>
/// A single tool invocation dispatched by the planner.
/// Tool names correspond to registered ITool instances (e.g. "MoveTo", "MineBlock").
/// </summary>
public record ActionData
{
    public string Tool { get; init; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; init; } = [];
}

/// <summary>Result returned by a tool after execution.</summary>
public record ToolResult(bool Success, string? Message = null, Dictionary<string, object?>? Data = null);

/// <summary>A wiki page search hit from MemorySmith.</summary>
public record SearchResult(string PageId, double Score, string? Snippet = null);

/// <summary>An event pushed from the world adapter (e.g. health changed, block mined).</summary>
public record WorldEvent(string EventType, Dictionary<string, object?> Payload, DateTimeOffset OccurredAt);

/// <summary>High-level goal metadata returned by the LLM planner.</summary>
public record GoalMeta(string Name, string Description, string[] Phases);
