namespace Agent.Planning;

using System.Text.Json;
using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;
using Agent.Planning.Llm;
using Agent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Hybrid HTN planner implementation.
///
/// Sprint 27 P0-D: type-switch branches for <see cref="IItemSpecGoal"/>,
/// <see cref="BuildGoal"/>, and <see cref="CraftItemGoal"/> have been removed.
/// Those are now handled by registered <see cref="IGoalDecomposer"/> implementations
/// (<see cref="GatherGoalDecomposer"/>, <see cref="BuildGoalDecomposer"/>,
/// <see cref="CraftItemGoalDecomposer"/>) through the <see cref="PlannerRouter"/>
/// which checks the <see cref="DecomposerRegistry"/> first.
///
/// <see cref="HtnPlanner"/> is now a pure fallback that handles goals by:
///   1. Direct task-library name match.
///   2. Phase-by-phase decomposition.
///   3. Throw if no actions result (caller falls back to LLM).
///
/// In production the agent loop uses <see cref="IPlanner"/> â†’ <see cref="PlannerRouter"/>
/// which routes typed goals to their decomposers before reaching <see cref="HtnPlanner"/>.
/// <see cref="HtnPlanner"/> therefore only receives goals with no registered decomposer.
/// </summary>
public sealed class HtnPlanner : IPlanner
{
    private readonly HtnTaskLibrary _library;
    private readonly ILogger<HtnPlanner> _logger;
    private readonly ILlmProvider? _llm;
    private readonly ToolDispatcher? _tools;

    public HtnPlanner(
        HtnTaskLibrary library,
        ILogger<HtnPlanner>? logger = null,
        ILlmProvider? llmProvider = null,
        ToolDispatcher? toolDispatcher = null)
    {
        _library = library;
        _logger = logger ?? NullLogger<HtnPlanner>.Instance;
        _llm = llmProvider;
        _tools = toolDispatcher;
    }

    // â”€â”€ PlanAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public Task<IPlan> PlanAsync(
        IGoal goal, WorldState state, CancellationToken cancellationToken = default)
    {
        var actions = new List<ActionData>();

        if (goal is BuildGoal buildGoal)
        {
            // TSK-0107: construct BuildOrigin from facts for the decomposer.
            // TSK-0116: removed IsCreativeMode branch — HtnTaskLibrary.DecomposeBuild
            // already handles creative mode internally. The PlannerRouter prefers
            // BuildGoalDecomposer first, making this HtnPlanner path a fallback.
            var origin = new BuildOrigin(
                ReadOriginFact(state, buildGoal.Blueprint.Id, "x"),
                ReadOriginFact(state, buildGoal.Blueprint.Id, "y"),
                ReadOriginFact(state, buildGoal.Blueprint.Id, "z"),
                BuildOriginSource.AutoScanned);

            actions.AddRange(_library.DecomposeBuild(
                buildGoal.Blueprint,
                buildGoal.Blocks,
                origin,
                state));
        }
        else if (goal is CraftItemGoal craftGoal)
        {
            actions.AddRange(_library.DecomposeCraftItem(craftGoal.ItemId, craftGoal.Count, state));
        }
        else if (goal is IItemSpecGoal itemSpecGoal)
        {
            actions.AddRange(_library.DecomposeGatherItem(
                itemSpecGoal.Spec,
                [itemSpecGoal.TargetCount.ToString()],
                state));
        }
        else if (_library.HasTask(goal.Name))
        {
            actions.AddRange(_library.Decompose(goal.Name, [], state));
        }
        else
        {
            // 2. Phase-by-phase decomposition (pure HTN fallback).
            // Typed goals now decompose directly above for compatibility with the
            // older tests and direct callers that still invoke HtnPlanner directly.
            foreach (var phase in goal.Phases)
                if (_library.HasTask(phase))
                    actions.AddRange(_library.Decompose(phase, [], state));
        }

        if (actions.Count == 0)
        {
            // Sprint 55: LLM fallback — call the language model to generate actions
            // when no decomposer or task-library method can handle the goal.
            var llmActions = TryLlmFallback(goal, state);
            if (llmActions is { Count: > 0 })
            {
                _logger.LogWarning(
                    "[planner] LLM fallback produced {Count} action(s) for goal '{Goal}'",
                    llmActions.Count, goal.Name);
                return Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases.ToArray(), llmActions));
            }

            throw new InvalidOperationException(
                $"HtnPlanner could not decompose goal '{goal.Name}' " +
                $"(phases: [{string.Join(", ", goal.Phases)}]). " +
                "No matching task methods found and LLM fallback failed or is unavailable.");
        }

        return Task.FromResult<IPlan>(new ActionPlan(goal.Name, goal.Phases.ToArray(), actions));
    }

    // Sprint 44 (TSK-0080): SearchMemory: removed â€” results were never consumed.
    // PreservedContextPrefixes keeps CraftItem:, FindFlatArea:, Build:, MoveTo: results.
    private static readonly string[] PreservedContextPrefixes =
        ["CraftItem:", "FindFlatArea:", "Build:", "MoveTo:"];

    private static IReadOnlyList<ActionData> CreateCreativeBuildActions(
        BuildGoal buildGoal, WorldState state, int originX, int originY, int originZ)
    {
        var actions = new List<ActionData>
        {
            // Sprint 44 (TSK-0080): SearchMemory removed â€” results were never consumed downstream.
            ActionFactory.Create("MoveTo", ("x", (object?)originX), ("y", (object?)originY), ("z", (object?)originZ)),
        };

        var progressKey = BuildFactKeys.BuildProgressIndex(buildGoal.Blueprint.Name);
        var checkpointIndex = 0;
        if (TryGetIntFactFromState(state, progressKey, out var lastPlaced))
            checkpointIndex = lastPlaced + 1;

        var executor = new BlueprintExecutor();
        var blockActions = executor.Execute(buildGoal.Blocks, originX, originY, originZ);

        for (var i = checkpointIndex; i < blockActions.Count; i++)
        {
            var placeAction = blockActions[i];
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlueprintId] = buildGoal.Blueprint.Name;
            placeAction.Context[BuildFactKeys.PlaceBlockProgressBlockIndex] = i;
            actions.Add(placeAction);
        }

        actions.Add(ActionFactory.Create("GetStatus"));
        return actions;
    }

    private static int ReadOriginFact(WorldState state, string blueprintId, string axis)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        if (!state.Facts.TryGetValue(key, out var v))
            return 0;

        return v switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
    }


    private static bool TryGetIntFactFromState(WorldState state, string key, out int result)
    {
        result = 0;
        if (!state.Facts.TryGetValue(key, out var v)) return false;
        return v switch
        {
            int i => (result = i) != int.MinValue,
            long l => (result = (int)l) != int.MinValue,
            double d => (result = (int)d) != int.MinValue,
            string s => int.TryParse(s, out result),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.TryGetInt32(out result),
            _ => false,
        };
    }

    // â”€â”€ ReplanAsync (TSK-0104: ReplanResult + ReplanGoalContext) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // ── LLM Fallback (Sprint 55) ────────────────────────────────────────────────

    /// <summary>
    /// When no decomposer or task-library method can handle a goal, ask the LLM
    /// to generate actions directly. Returns null if the LLM is unavailable or fails.
    /// </summary>
    private IReadOnlyList<ActionData>? TryLlmFallback(IGoal goal, WorldState state)
    {
        if (_llm is not { IsAvailable: true })
        {
            _logger.LogDebug("[planner] LLM fallback skipped — provider unavailable");
            return null;
        }

        var toolNames = _tools?.RegisteredNames is { Count: > 0 } names
            ? string.Join(", ", names)
            : "GetStatus, Chat, MoveTo";

        var prompt = string.Concat(
            "You are a Minecraft bot planner. Generate actions for a goal.\n\n",
            $"Goal: {goal.Name}\n",
            $"Description: {goal.Description}\n",
            $"Phases: [{string.Join(", ", goal.Phases)}]\n\n",
            $"Bot state: position=({state.Position.X},{state.Position.Y},{state.Position.Z}), ",
            $"health={state.Health}/20, gameMode={state.GameMode}\n\n",
            $"Available tools: {toolNames}\n\n",
            "Reply ONLY with a JSON array of action objects. Each action has \"tool\" (string) ",
            "and \"args\" (object with parameter names and values).\n",
            "Example: [{\"tool\":\"MoveTo\",\"args\":{\"x\":10,\"y\":64,\"z\":20}},{\"tool\":\"GetStatus\"}]\n",
            "If you cannot determine appropriate actions, reply with: []");

        try
        {
            var response = _llm.CompleteAsync(prompt, "", CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("[planner] LLM fallback returned empty response for goal '{Goal}'", goal.Name);
                return null;
            }

            return ParseLlmActions(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[planner] LLM fallback failed for goal '{Goal}'", goal.Name);
            return null;
        }
    }

    private static IReadOnlyList<ActionData>? ParseLlmActions(string llmResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(llmResponse.Trim());
            var actions = new List<ActionData>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("tool", out var toolEl)) continue;
                var tool = toolEl.GetString();
                if (string.IsNullOrWhiteSpace(tool)) continue;

                var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (el.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsEl.EnumerateObject())
                        args[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.GetRawText(),
                        };
                }

                actions.Add(new ActionData { Tool = tool, Arguments = args });
            }
            return actions.Count > 0 ? actions : null;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[planner] LLM fallback JSON parse error: {ex.Message}");
            return null;
        }
    }

    public async Task<ReplanResult> ReplanAsync(
        ReplanGoalContext context, CancellationToken cancellationToken = default)
    {
        // Sprint 32 P2-2: thread failureReason to log so it is visible in telemetry.
        // Two independent audits (Sprint 25 deep-code-audit + Sprint 32 refinement-audit)
        // both flagged this parameter as accepted but unused. Adaptive replanning that
        // acts on the reason (e.g. choose alternative decomposer) is future scope.
        if (!string.IsNullOrWhiteSpace(context.FailureReason))
            _logger.LogInformation(
                "ReplanAsync triggered for goal '{GoalName}' with reason: {FailureReason}",
                context.CurrentPlan.GoalName, context.FailureReason);

        var preservedContext = new Dictionary<string, object?>();
        foreach (var action in context.CurrentPlan.Actions)
            foreach (var (key, value) in action.Context)
                if (Array.Exists(PreservedContextPrefixes,
                        prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                    preservedContext.TryAdd(key, value);

        var goal = new SimpleGoal(
            context.CurrentPlan.GoalName, "",
            [.. context.CurrentPlan.Phases],
            _ => false);

        try
        {
            var newPlan = await PlanAsync(goal, context.State, cancellationToken);

            if (preservedContext.Count > 0)
                foreach (var newAction in (newPlan as ActionPlan)?.Actions ?? newPlan.Actions)
                    foreach (var (key, value) in preservedContext)
                        newAction.Context.TryAdd(key, value);

            return ReplanResult.Success(newPlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HtnPlanner.ReplanAsync: Planner error for goal '{Goal}'",
                context.CurrentPlan.GoalName);
            return ReplanResult.Failure($"Planner error: {ex.Message}");
        }
    }
}
