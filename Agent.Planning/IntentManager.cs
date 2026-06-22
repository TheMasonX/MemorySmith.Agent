namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Sprint 37 P1-A: Maps an <see cref="IntentDraft"/> to a <see cref="GoalRequest"/>
/// that <c>GoalFactory.CreateAsync</c> can consume.
///
/// Enforces AGENTS.md CRITICAL rule: parsers never create goals.
///
/// Before Sprint 37:
///   LlmChatInterpreter.ParseDecision contained a switch statement that mapped
///   intent strings → goal names ("gather" → "GatherItem:{item}", etc.).
///   This violated PRINCIPLE-1 because the PARSER was creating goal-name strings.
///
/// After Sprint 37:
///   ParseDecision populates an IntentDraft (pure semantic data, no goal name).
///   IntentManager.BuildGoalRequest converts that draft to (GoalName, Parameters).
///   GoalFactory.CreateAsync creates the actual IGoal from those inputs.
///   The parser no longer knows what a "goal" is.
///
/// Pipeline:
///   Chat → LlmChatInterpreter → IntentDraft → IntentManager → GoalRequest → GoalFactory → IGoal
///
/// Sprint 38: IntentManager will implement IIntentManager (from Agent.Core.Runtime)
/// and be wired into the AgentRuntime decomposition.
/// </summary>
public sealed class IntentManager
{
    /// <summary>
    /// Converts the semantic intent in <paramref name="draft"/> to a
    /// <see cref="GoalRequest"/> for <c>GoalFactory.CreateAsync</c>.
    ///
    /// Returns <see langword="null"/> when the intent maps to no goal
    /// (conversation, status, cancel, clarify, ignore, etc.).
    /// </summary>
    public GoalRequest? BuildGoalRequest(IntentDraft draft)
    {
        switch (draft.Intent.ToLowerInvariant())
        {
            case "gather":
                if (draft.Item is not null)
                    return new GoalRequest(
                        $"GatherItem:{draft.Item}",
                        new Dictionary<string, object?> { ["count"] = draft.Count ?? 10 });
                break;

            case "craft":
                if (draft.Item is not null)
                    return new GoalRequest(
                        $"CraftItem:{draft.Item}",
                        new Dictionary<string, object?> { ["count"] = draft.Count ?? 1 });
                break;

            case "build":
                if (draft.Blueprint is not null)
                {
                    Dictionary<string, object?>? parameters = null;
                    if (draft.X is not null && draft.Y is not null && draft.Z is not null)
                    {
                        parameters = new Dictionary<string, object?>
                        {
                            ["originX"] = draft.X,
                            ["originY"] = draft.Y,
                            ["originZ"] = draft.Z,
                        };
                    }
                    return new GoalRequest($"Build:{draft.Blueprint}", parameters);
                }
                break;

            case "navigate":
                if (draft.X is not null && draft.Y is not null && draft.Z is not null)
                    return new GoalRequest(
                        "MoveTo",
                        new Dictionary<string, object?>
                        {
                            ["x"] = draft.X,
                            ["y"] = draft.Y,
                            ["z"] = draft.Z,
                        });
                break;
        }
        return null;
    }
}

/// <summary>
/// Sprint 37 P1-A: A resolved (GoalName, Parameters) pair ready for
/// <c>GoalFactory.CreateAsync</c>.
///
/// Produced by <see cref="IntentManager.BuildGoalRequest"/>; consumed by
/// <c>AgentBackgroundService.TryCreateGoalFromChatAsync</c>.
/// Replaces the inline goal-name strings previously computed inside ParseDecision.
/// </summary>
public sealed record GoalRequest(
    /// <summary>GoalFactory key (e.g. "GatherItem:oak_log", "Build:small-house").</summary>
    string GoalName,
    /// <summary>Optional parameters forwarded to GoalFactory.CreateAsync.</summary>
    IReadOnlyDictionary<string, object?>? Parameters);
