namespace Agent.Planning;

using Agent.Core;

/// <summary>
/// Maps <see cref="IntentDraft"/> to a typed <see cref="GoalRequest"/> suitable
/// for the GoalFactory pipeline (PRINCIPLE-1: parsers never create goals).z///
/// Sprint 39 P3: GoalRequest refactored from a single loosely-typed record to an
/// abstract base + typed subclasses (GatherGoalRequest, CraftGoalRequest,
/// BuildGoalRequest, NavigateGoalRequest). Each subclass encodes its own parameters
/// as typed fields and exposes them as IReadOnlyDictionary via the virtual Parameters
/// property for backward-compat with GoalFactory.CreateAsync.
/// </summary>
public sealed class IntentManager
{
    /// <summary>
    /// Maps <paramref name="draft"/> to a typed <see cref="GoalRequest"/>, or null
    /// when the intent does not produce a goal (cancel, status, help, etc.).
    /// </summary>
    public GoalRequest? BuildGoalRequest(IntentDraft draft)
    {
        switch (draft.Intent.ToLowerInvariant())
        {
            case "gather":
                if (draft.Item is not null)
                    return new GatherGoalRequest(draft.Item, draft.Count ?? 10);
                break;
            case "craft":
                if (draft.Item is not null)
                    return new CraftGoalRequest(draft.Item, draft.Count ?? 1);
                break;
            case "build":
                if (draft.Blueprint is not null)
                    return new BuildGoalRequest(
                        draft.Blueprint,
                        draft.X, draft.Y, draft.Z);
                break;
            case "navigate":
                if (draft.X is not null && draft.Y is not null && draft.Z is not null)
                    return new NavigateGoalRequest(draft.X.Value, draft.Y.Value, draft.Z.Value);
                break;
        }
        return null;
    }
}

// ── Typed GoalRequest hierarchy  ─────────────────────────────────────────────

/// <summary>
/// Sprint 39 P3: Abstract base for all typed goal requests.
/// Enforces PRINCIPLE-1 (parsers never create goals): interpreters produce
/// IntentDraft; BuildGoalRequest maps to a typed GoalRequest; GoalFactory
/// maps GoalRequest.GoalName → IGoal.
///
/// Parameters is a virtual property for backward-compat with
/// GoalFactory.CreateAsync(string goalName, IReadOnlyDictionary parameters).
/// Typed subclasses override it to expose their fields as a dictionary.
/// </summary>
public abstract record GoalRequest(string GoalName)
{
    /// <summary>
    /// Parameters as a loosely-typed dictionary for GoalFactory backward compat.
    /// Typed subclasses override this to provide their specific parameter set.
    /// </summary>
    public abstract IReadOnlyDictionary<string, object?>? Parameters { get; }
}

/// <summary>Sprint 39 P3: gather N items of a given type.</summary>
public sealed record GatherGoalRequest(string Item, int Count = 10)
    : GoalRequest($"GatherItem:{Item}")
{
    public override IReadOnlyDictionary<string, object?>? Parameters =>
        new Dictionary<string, object?> { ["count"] = Count };
}

/// <summary>Sprint 39 P3: craft N items of a given type.</summary>
public sealed record CraftGoalRequest(string Item, int Count = 1)
    : GoalRequest($"CraftItem:{Item}")
{
    public override IReadOnlyDictionary<string, object?>? Parameters =>
        new Dictionary<string, object?> { ["count"] = Count };
}

/// <summary>
/// Sprint 39 P3: build a blueprint at an optional origin.
/// When OriginX/Y/Z are null, the agent resolves the origin via FindFlatArea
/// (OriginSource.AutoScanned) or the player's current position (OriginSource.PlayerPosition).
/// </summary>
public sealed record BuildGoalRequest(
    string Blueprint,
    int? OriginX = null,
    int? OriginY = null,
    int? OriginZ = null) : GoalRequest($"Build:{Blueprint}")
{
    public override IReadOnlyDictionary<string, object?>? Parameters
    {
        get
        {
            if (OriginX is null || OriginZ is null || OriginZ is null)
                return null;
            return new Dictionary<string, object?>
            {
                ["originX"] = OriginX,
                ["originY"] = OriginY,
                ["originZ"] = OriginZ,
            };
        }
    }
}

/// <summary>Sprint 39 P3: navigate the bot to an explicit coordinate.</summary>
public sealed record NavigateGoalRequest(int X, int Y, int Z)
    : GoalRequest("MoveTo")
{
    public override IReadOnlyDictionary<string, object?>? Parameters =>
        new Dictionary<string, object?> { ["x"] = X, ["y"] = Y, ["z"] = Z };
}
