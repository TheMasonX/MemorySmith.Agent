namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

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
    // ── Alias dictionaries ───────────────────────────────────────────────────
    // TSK-0099: consolidated in AliasRegistry. Item and blueprint aliases are
    // sourced from AliasRegistry.ItemAliases and AliasRegistry.BlueprintAliases.
    // Blueprint aliases map common user-facing names to canonical blueprint IDs.
    // Item aliases merge ChatInterpreter player-shorthand mappings with
    // IntentManager LLM-output normalization entries (wool→white_wool, etc.).

    /// <summary>
    /// Maps <paramref name="draft"/> to a typed <see cref="GoalRequest"/>, or null
    /// when the intent does not produce a goal (cancel, status, help, etc.).
    ///
    /// Sprint 44 (TSK-0079): added "smelt" case — routes to <see cref="SmeltGoalRequest"/>
    /// instead of falling through to <c>CraftItemGoal</c>. The adapter has a dedicated
    /// <c>case 'smelt':</c> handler that was never exercised because every smelt intent
    /// was previously mapped to CraftItemGoal → CraftItemTool.
    /// </summary>
    public GoalRequest? BuildGoalRequest(IntentDraft draft)
    {
        var intent = draft.Intent.ToLowerInvariant();

        // Sprint 44 (TSK-0079): "smelt" intent maps to SmeltGoalRequest (separate from craft).
        // The LLM may return "smelt" intent with item="iron_ore" or item="raw_iron".
        if (intent == "smelt")
        {
            if (draft.Item is not null)
                return new SmeltGoalRequest(ResolveItem(draft.Item), draft.Count ?? 1);
            return null;
        }

        switch (intent)
        {
            case "gather":
                if (draft.Item is not null)
                    return new GatherGoalRequest(ResolveItem(draft.Item), draft.Count ?? 10);
                break;
            case "place":
                // Sprint 54: single/multi block placement without a blueprint.
                // Item is required; coords are optional (null = place in front).
                if (draft.Item is not null)
                    return new PlaceGoalRequest(ResolveItem(draft.Item), draft.Count ?? 1,
                        draft.X, draft.Y, draft.Z);
                break;
            case "craft":
                if (draft.Item is not null)
                    return new CraftGoalRequest(ResolveItem(draft.Item), draft.Count ?? 1);
                break;
            case "build":
                return TryBuildGoal(draft);
            // Navigate is handled directly in HandleChatEventAsync (case "navigate")
            // with player-position fallback for null coords. This case is a secondary
            // path for callers that require a GoalRequest; it returns null when coords
            // are missing, which is correct since NavigateGoalRequest requires all 3.
            case "navigate":
                if (draft.X is not null && draft.Y is not null && draft.Z is not null)
                    return new NavigateGoalRequest(draft.X.Value, draft.Y.Value, draft.Z.Value);
                break;
        }
        return null;
    }

    /// <summary>
    /// Sprint 41: extracted build goal creation so it can be reused without goto.
    /// Resolves common blueprint names (e.g. "house" → "small-house") via
    /// BlueprintAliases so the LLM doesn't need to know exact internal IDs.
    /// </summary>
    private static GoalRequest? TryBuildGoal(IntentDraft draft)
    {
        var resolved = ResolveBlueprint(draft.Blueprint);
        if (resolved is not null)
            return new BuildGoalRequest(resolved, BuildOrigin.FromNullable(draft.X, draft.Y, draft.Z));
        return null;
    }

    /// <summary>
    /// Resolves a blueprint name through <see cref="AliasRegistry.BlueprintAliases"/>,
    /// or returns it as-is if no alias exists. Returns null for null input.
    /// </summary>
    private static string? ResolveBlueprint(string? blueprint)
    {
        if (blueprint is null) return null;
        if (AliasRegistry.BlueprintAliases.TryGetValue(blueprint, out var alias))
            return alias;
        return blueprint;
    }

    /// <summary>
    /// Sprint 43 (P1-1): Resolves a user-facing item name to its canonical Minecraft ID
    /// through <see cref="AliasRegistry.ItemAliases"/>, or returns it as-is if no alias exists.
    /// This handles cases like "wool" → "white_wool" that the LLM may return.
    /// </summary>
    private static string ResolveItem(string item)
    {
        if (AliasRegistry.ItemAliases.TryGetValue(item, out var alias))
            return alias;
        return item;
    }

    // ── Command string parsing for multi-step chaining (TSK-0205) ────────────

    /// <summary>
    /// Parses a simple command string from <see cref="IntentDraft.NextSteps"/>
    /// into a typed <see cref="GoalRequest"/>. Supports basic patterns like
    /// "craft 20 planks", "gather 5 oak_log", "build small-house", "smelt 3 iron_ore".
    /// Returns null for unrecognized commands.
    /// </summary>
    public static GoalRequest? ParseCommandString(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        // "craft N item" or "craft item"
        var craftMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
            @"^craft\s+(?:(?<count>\d+)\s+)?(?<item>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (craftMatch.Success)
        {
            var item = ResolveItem(craftMatch.Groups["item"].Value.Trim());
            var count = craftMatch.Groups["count"].Success
                ? int.Parse(craftMatch.Groups["count"].Value) : 1;
            return new CraftGoalRequest(item, count);
        }

        // "gather N item" / "mine N item" / "get N item"
        var gatherMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
            @"^(?:gather|mine|get)\s+(?:(?<count>\d+)\s+)?(?<item>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (gatherMatch.Success)
        {
            var item = ResolveItem(gatherMatch.Groups["item"].Value.Trim());
            var count = gatherMatch.Groups["count"].Success
                ? int.Parse(gatherMatch.Groups["count"].Value) : 10;
            return new GatherGoalRequest(item, count);
        }

        // "build blueprint"
        var buildMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
            @"^build\s+(?:a\s+)?(?<blueprint>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (buildMatch.Success)
        {
            var bp = ResolveBlueprint(buildMatch.Groups["blueprint"].Value.Trim());
            if (bp is not null)
                return new BuildGoalRequest(bp, null);
        }

        // "place N item" / "place item at X Y Z" / "place item in front"
        var placeMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
            @"^place\s+(?:(?<count>\d+)\s+)?(?<item>.+?)(?:\s+at\s+(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (placeMatch.Success)
        {
            var item = ResolveItem(placeMatch.Groups["item"].Value.Trim());
            var count = placeMatch.Groups["count"].Success
                ? int.Parse(placeMatch.Groups["count"].Value) : 1;
            int? x = placeMatch.Groups["x"].Success ? int.Parse(placeMatch.Groups["x"].Value) : null;
            int? y = placeMatch.Groups["y"].Success ? int.Parse(placeMatch.Groups["y"].Value) : null;
            int? z = placeMatch.Groups["z"].Success ? int.Parse(placeMatch.Groups["z"].Value) : null;
            return new PlaceGoalRequest(item, count, x, y, z);
        }

        // "move to X Y Z" / "go to X Y Z" / "navigate to X Y Z"
        var moveMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
            @"^(?:move|go|navigate|walk)\s+(?:to\s+)?(?<x>-?\d+)\s+(?<y>-?\d+)\s+(?<z>-?\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (moveMatch.Success)
        {
            return new NavigateGoalRequest(
                int.Parse(moveMatch.Groups["x"].Value),
                int.Parse(moveMatch.Groups["y"].Value),
                int.Parse(moveMatch.Groups["z"].Value));
        }

        // "smelt N item" / "smelt item"
        var smeltMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
            @"^smelt\s+(?:(?<count>\d+)\s+)?(?<item>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (smeltMatch.Success)
        {
            var item = ResolveItem(smeltMatch.Groups["item"].Value.Trim());
            var count = smeltMatch.Groups["count"].Success
                ? int.Parse(smeltMatch.Groups["count"].Value) : 1;
            return new SmeltGoalRequest(item, count);
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
/// TSK-0103: Origin consolidated into <see cref="BuildOrigin"/> value object.
/// When Origin is null, the agent resolves the origin via FindFlatArea
/// (BuildOriginSource.AutoScanned) or the player's current position.
/// </summary>
public sealed record BuildGoalRequest(
    string Blueprint,
    BuildOrigin? Origin = null) : GoalRequest($"Build:{Blueprint}")
{
    public override IReadOnlyDictionary<string, object?>? Parameters
    {
        get
        {
            if (Origin is null)
                return null;
            return new Dictionary<string, object?>
            {
                ["originX"] = Origin.X,
                ["originY"] = Origin.Y,
                ["originZ"] = Origin.Z,
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

/// <summary>
/// Sprint 44 (TSK-0079): smelt N units of an item.
/// Maps to <see cref="SmeltGoal"/> via GoalFactory → <see cref="SmeltGoalDecomposer"/>.
/// Separated from <c>CraftGoalRequest</c> so the planner emits <c>SmeltItem</c> actions
/// instead of <c>CraftItem</c>, exercising the adapter's dedicated smelt handler.
/// </summary>
public sealed record SmeltGoalRequest(string InputItem, int Count = 1)
    : GoalRequest($"SmeltItem:{InputItem}")
{
    public override IReadOnlyDictionary<string, object?>? Parameters =>
        new Dictionary<string, object?> { ["count"] = Count };
}

/// <summary>
/// Sprint 54: place N blocks at an optional position.
/// When X/Y/Z are null, the agent places the block in front of itself.
/// Maps to a single PlaceBlock action dispatched directly (no blueprint needed).
/// Separated from <c>BuildGoalRequest</c> so the LLM can distinguish
/// "place a block" from "build a house".
/// </summary>
public sealed record PlaceGoalRequest(string Item, int Count = 1, int? X = null, int? Y = null, int? Z = null)
    : GoalRequest($"PlaceBlock:{Item}")
{
    public override IReadOnlyDictionary<string, object?>? Parameters =>
        new Dictionary<string, object?>
        {
            ["count"] = Count,
            ["x"] = X,
            ["y"] = Y,
            ["z"] = Z,
        };
}
