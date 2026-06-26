namespace Agent.Core;

/// <summary>
/// A single tool invocation dispatched by the planner.
/// Tool names correspond to registered ITool instances (e.g. "MoveTo", "MineBlock").
///
/// Context carry (Phase 4): tools can write results into Context so subsequent
/// actions in the same plan can read them. For example, SearchMemoryTool writes
/// the first result's coordinates to Context["nearestWoodX/Y/Z"], and MoveToTool
/// reads them if its own Arguments don't specify coordinates.
/// </summary>
public record ActionData
{
    public string Tool { get; init; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; init; } = [];

    /// <summary>
    /// Mutable inter-action context bag. Shared across all actions in a single plan
    /// dispatch sequence. Tools write here; subsequent tools read here.
    /// </summary>
    public Dictionary<string, object?> Context { get; init; } = [];
}

/// <summary>Result returned by a tool after execution.</summary>
/// <param name="Success">Whether the tool call succeeded at the transport level.</param>
/// <param name="Message">Human-readable result description.</param>
/// <param name="Data">Optional structured data returned by the tool.</param>
/// <param name="Outcome">
/// Rich outcome type providing semantic detail about WHY an action succeeded or failed.
/// Defaults to <see cref="OutcomeType.Completed"/> for backward compatibility;
/// tools that need to report Blocked, Unreachable, TimedOut, or NoProgress should set this explicitly.
/// <c>CallWithOutcomeAsync</c> maps this value to the corresponding <see cref="ActionOutcome"/> factory method.
/// </param>
public record ToolResult(bool Success, string? Message = null,
    Dictionary<string, object?>? Data = null,
    OutcomeType Outcome = OutcomeType.Completed);

/// <summary>
/// A search hit from MemorySmith.
/// Kind is "page" (PageId is a slug, readable via GetPageAsync) or
/// "memory" (PageId is a UUID, NOT a valid page slug).
/// Always check Kind before passing PageId to GetPageAsync.
/// </summary>
public record SearchResult(string PageId, double Score, string? Snippet = null, string Kind = "page");

/// <summary>High-level goal metadata returned by the LLM planner.</summary>
public record GoalMeta(string Name, string Description, string[] Phases);
