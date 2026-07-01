namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;
using Agent.Planning.Llm;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

/// <summary>
/// Sprint 39 P1: Concrete LLM-backed implementation of <see cref="Agent.Core.ILlmEvaluator"/>.
///
/// Evaluation strategy:
///   1. Skip if fewer than <see cref="MinOutcomesBeforeEval"/> outcomes (avoids per-action LLM calls).
///   2. Skip if all outcomes succeeded (fast-path: no reason to replan on full success).
///   3. Skip if LLM provider is unavailable.
///   4. Call the LLM with a compact prompt: goal name + world snapshot + last 10 outcomes.
///   5. Parse response JSON: { "replan": true|false, "reason": "..." }.
///   6. On any error (network, parse, timeout) → false (conservative: continue current plan).
///
/// This closes the observation-driven replanning loop introduced in Sprint 35:
///   Plan → Execute → ActionOutcome → LLM Evaluate → Replan?
///
/// Wired from AgentBackgroundService.DispatchActionsAsync after each ActionOutcome
/// is enqueued into _cycleOutcomes (Sprint 39 P1 wiring, replaces TODO comment).
/// </summary>
public sealed class LlmEvaluatorImpl : ILlmEvaluator
{
    private readonly ILlmProvider _provider;
    private readonly ILogger<LlmEvaluatorImpl> _logger;

    /// <summary>
    /// Minimum accumulated outcomes in _cycleOutcomes before the evaluator
    /// bothers calling the LLM. Prevents a wall of LLM calls when the plan starts.
    /// </summary>
    private const int MinOutcomesBeforeEval = 3;

    public LlmEvaluatorImpl(ILlmProvider provider, ILogger<LlmEvaluatorImpl> logger)
    {
        _provider = provider;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public async Task<EvaluationResult> EvaluateAsync(
        IGoal goal,
        IReadOnlyList<ActionOutcome> outcomes,
        WorldState worldState,
        CancellationToken ct = default,
        bool forceEvaluate = false,
        WorldStateDiff? diff = null)
    {
        // Fast-path 1: too few data points to make a reliable judgement.
        // Sprint 54 (TSK-0222): skipped when forceEvaluate=true (governor stall).
        if (!forceEvaluate && outcomes.Count < MinOutcomesBeforeEval)
            return new EvaluationResult(false, "too few outcomes");

        // Fast-path 2: all outcomes succeeded — no reason to replan.
        // Sprint 54 (TSK-0222): skipped when forceEvaluate=true. Fire-and-forget
        // tools (place, MineBlock) always report success at dispatch time; their
        // real failures show up later in correlated action timeouts, not outcomes.
        // Sprint 58 Wave C (TSK-0320): don't fast-path when the world diverged
        // from expectations. If diff.HasMismatch, the LLM must evaluate whether
        // the divergence warrants a replan even though all outcomes claim success.
        var failureCount = outcomes.Count(static o => !o.Success);
        if (!forceEvaluate && failureCount == 0 && (diff is null || !diff.HasMismatch))
            return new EvaluationResult(false, "all actions succeeded");

        // Fast-path 3: provider offline — skip silently.
        if (!_provider.IsAvailable)
        {
            _logger.LogDebug(
                "[evaluator] provider '{Provider}' unavailable — skipping evaluation for goal {Goal}",
                _provider.ProviderName, goal.Name);
            return new EvaluationResult(false, "provider unavailable")
            {
                IsSuccess = false,
                FailureReason = "ProviderUnavailable"
            };
        }

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userMessage  = BuildUserMessage(goal, outcomes, worldState, diff);

            var raw = await _provider.CompleteAsync(systemPrompt, userMessage, ct);

            if (raw is null)
            {
                _logger.LogWarning(
                    "[evaluator] provider returned null for goal {Goal} — defaulting to no-replan",
                    goal.Name);
                return new EvaluationResult(false, "null response")
                {
                    IsSuccess = false,
                    FailureReason = "NullResponse"
                };
            }

            var result = ParseEvaluationResult(raw);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "[evaluator] parse failure for goal {Goal}: {Reason} (rawLen={RawLen})",
                    goal.Name, result.Reason, raw.Length);
            }
            else if (result.ShouldReplan)
                _logger.LogInformation(
                    "[evaluator] recommends replan for goal {Goal} — {Count} outcomes, {Failures} failures. Suggestion: {Suggestion}",
                    goal.Name, outcomes.Count, failureCount, result.Suggestion);
            else
                _logger.LogDebug(
                    "[evaluator] says continue for goal {Goal} — {Count} outcomes, {Failures} failures. Reason: {Reason}",
                    goal.Name, outcomes.Count, failureCount, result.Reason);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // propagate — caller owns the lifetime token
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[evaluator] unexpected error during evaluation — defaulting to no-replan for goal {Goal}",
                goal.Name);
            return new EvaluationResult(false, $"error: {ex.Message}")
            {
                IsSuccess = false,
                FailureReason = ex is TimeoutException ? "Timeout" : "ParseFailure"
            };
        }
    }

    // ── Prompt construction ───────────────────────────────────────────────────

    private static string BuildSystemPrompt() =>
        "You are an autonomous Minecraft agent evaluator. Decide whether the agent's " +
        "current plan is failing and a new plan should be requested.\n\n" +
        "Respond ONLY with compact JSON:\n" +
        "  { \"replan\": true, \"reason\": \"...\", \"suggestion\": \"...\" }  — abandon and replan\n" +
        "  { \"replan\": false, \"reason\": \"...\" }                         — continue executing\n\n" +
        "Recommend replan (true) when: multiple consecutive failures on the same tool, " +
        "a required resource that clearly does not exist, or a goal that cannot be completed " +
        "given the current world state.\n" +
        "Do NOT replan for a single transient failure or minor setbacks.\n" +
        "When replan=true, include a specific \"suggestion\" for remediation (e.g., " +
        "\"skip block #9\", \"step back 3 blocks and retry\", " +
        "\"clear plan and move to origin before rebuilding\").\n" +
        "Keep \"reason\" and \"suggestion\" under 20 words each.";

    private static string BuildUserMessage(
        IGoal goal,
        IReadOnlyList<ActionOutcome> outcomes,
        WorldState worldState,
        WorldStateDiff? diff = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Goal: {goal.Name} ({goal.GetType().Name})");
        sb.AppendLine(
            $"World: HP={worldState.Health}/20, Food={worldState.Food}/20, " +
            $"Pos=({worldState.Position.X},{worldState.Position.Y},{worldState.Position.Z}), " +
            $"Inventory={worldState.Inventory.Count} distinct items");

        // Sprint 55 (TSK-0155): goal-type-agnostic context.
        // Report goal-specific progress so the LLM can evaluate ANY goal type,
        // not just builds.
        AppendGoalContext(sb, goal, worldState);

        // Sprint 55 (TSK-0155): include WorldStateDiff for observation comparison.
        if (diff is not null && diff.HasMismatch)
        {
            sb.AppendLine($"Observed mismatches: {diff.DescribeMismatches()}");
            if (diff.HasThreats)
                sb.AppendLine($"⚠ Threats detected: {string.Join(", ", diff.NewThreats!)}");
            if (diff.HealthDelta < 0)
                sb.AppendLine($"⚠ Health dropped by {Math.Abs(diff.HealthDelta)} HP during actions");
        }

        sb.AppendLine();
        sb.AppendLine("Recent outcomes (oldest first):");
        foreach (var o in outcomes.TakeLast(10))
            sb.AppendLine($"  [{(o.Success ? "OK  " : "FAIL")}] {o.ToolName}: {o.ObservationSummary}");
        return sb.ToString();
    }

    // ── Goal-type-agnostic context (Sprint 55 TSK-0155) ────────────────────

    private static void AppendGoalContext(StringBuilder sb, IGoal goal, WorldState worldState)
    {
        // Build goals: block-level progress + skip reasons + facing blocks
        if (goal is IBuildGoal bg)
        {
            var bpName = bg.Blueprint.Name;
            var totalBlocks = bg.Blocks.Count;
            var (placed, skipped, inProgress, pending) = CountBlockStatuses(bpName, totalBlocks, worldState);
            sb.AppendLine($"Build: {placed} placed, {skipped} skipped, {inProgress} in-progress, {pending} pending of {totalBlocks} total");

            var skipReasons = GetRecentSkipReasons(bpName, totalBlocks, worldState, max: 5);
            if (skipReasons.Count > 0)
                sb.AppendLine($"Recent skip reasons: {string.Join("; ", skipReasons)}");

            var facingBlocks = GetFacingSensitiveBlocks(bg, max: 5);
            if (facingBlocks.Count > 0)
                sb.AppendLine($"Facing-sensitive blocks: {string.Join(", ", facingBlocks)}");
        }
        // Gather goals: current inventory vs. target
        else if (goal is IItemSpecGoal itemGoal)
        {
            var spec = itemGoal.Spec;
            var current = 0;
            foreach (var block in spec.SourceBlocks)
            {
                var key = block.Contains(':') ? block[(block.IndexOf(':') + 1)..] : block;
                current += worldState.Inventory.GetValueOrDefault(key);
            }
            sb.AppendLine($"Gather: {current}/{itemGoal.TargetCount} {spec.ItemId} collected");
        }
        // Craft goals: do we have the item yet?
        else if (goal is CraftItemGoal craftGoal)
        {
            var have = worldState.Inventory.GetValueOrDefault(craftGoal.ItemId);
            sb.AppendLine($"Craft: {have}/{craftGoal.Count} {craftGoal.ItemId} available");
        }
        // Navigate goals: distance to target
        else if (goal.Name.StartsWith("Navigate:", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"Navigate: currently at ({worldState.Position.X},{worldState.Position.Y},{worldState.Position.Z})");
        }
    }

    // ── Build-aware helpers (Sprint 54 TSK-0217) ──────────────────────────

    private static (int placed, int skipped, int inProgress, int pending) CountBlockStatuses(
        string blueprintId, int totalBlocks, WorldState worldState)
    {
        var placed = 0; var skipped = 0; var inProgress = 0; var pending = 0;
        for (int i = 0; i < totalBlocks; i++)
        {
            var key = BuildFactKeys.BlockStatus(blueprintId, i);
            var status = worldState.Facts.TryGetValue(key, out var v) ? v?.ToString() : null;
            switch (status)
            {
                case BuildFactKeys.BlockStatusPlaced: placed++; break;
                case BuildFactKeys.BlockStatusSkipped: skipped++; break;
                case BuildFactKeys.BlockStatusInProgress: inProgress++; break;
                default: pending++; break;
            }
        }
        return (placed, skipped, inProgress, pending);
    }

    private static List<string> GetRecentSkipReasons(
        string blueprintId, int totalBlocks, WorldState worldState, int max)
    {
        var reasons = new List<string>();
        for (int i = 0; i < totalBlocks && reasons.Count < max; i++)
        {
            var skipKey = BuildFactKeys.SkipReason(blueprintId, i);
            if (worldState.Facts.TryGetValue(skipKey, out var v) && v?.ToString() is string reason && reason.Length > 0)
                reasons.Add($"#{i}:{reason}");
        }
        return reasons;
    }

    private static List<string> GetFacingSensitiveBlocks(IBuildGoal bg, int max)
    {
        // Blocks that have orientation-dependent placement: beds, doors, stairs, slabs, furnaces, etc.
        var facingSensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "oak_door", "spruce_door", "birch_door", "jungle_door", "acacia_door", "dark_oak_door",
            "iron_door", "crimson_door", "warped_door",
            "red_bed", "white_bed", "orange_bed", "magenta_bed", "light_blue_bed", "yellow_bed",
            "lime_bed", "pink_bed", "gray_bed", "light_gray_bed", "cyan_bed", "purple_bed",
            "blue_bed", "brown_bed", "green_bed", "black_bed",
            "furnace", "blast_furnace", "smoker",
            "oak_stairs", "spruce_stairs", "birch_stairs", "jungle_stairs", "stone_stairs",
            "cobblestone_stairs", "brick_stairs", "stone_brick_stairs",
            "oak_slab", "spruce_slab", "birch_slab", "stone_slab", "cobblestone_slab",
        };

        var result = new List<string>();
        for (int i = 0; i < bg.Blocks.Count && result.Count < max; i++)
        {
            if (facingSensitive.Contains(bg.Blocks[i].BlockId))
                result.Add($"#{i}:{bg.Blocks[i].BlockId}");
        }
        return result;
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    /// <summary>
    /// Sprint 56 (TSK-0278): Parses the LLM evaluation response into a structured result.
    /// Made internal for testability. Returns IsSuccess=false with specific FailureReason
    /// on parse failure rather than silently defaulting to "no replan."
    /// </summary>
    internal static EvaluationResult ParseEvaluationResult(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new EvaluationResult(false, "empty response")
            {
                IsSuccess = false,
                FailureReason = "ParseFailure"
            };
        }

        try
        {
            var json = ExtractJson(response);
            if (json is null)
            {
                // No JSON brackets found — prose-only or malformed response.
                return new EvaluationResult(false, "no JSON found in response")
                {
                    IsSuccess = false,
                    FailureReason = "ParseFailure"
                };
            }
            using var doc = JsonDocument.Parse(json);
            var shouldReplan = doc.RootElement.TryGetProperty("replan", out var p) && p.GetBoolean();
            var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            var suggestion = doc.RootElement.TryGetProperty("suggestion", out var s) ? s.GetString() ?? "" : "";
            return new EvaluationResult(shouldReplan, reason, suggestion);
        }
        catch (JsonException)
        {
            return new EvaluationResult(false, "invalid JSON in response")
            {
                IsSuccess = false,
                FailureReason = "ParseFailure"
            };
        }
        catch (Exception)
        {
            return new EvaluationResult(false, "unparseable response")
            {
                IsSuccess = false,
                FailureReason = "ParseFailure"
            };
        }
    }

    /// <summary>
    /// Extracts the first JSON object from an LLM response that may contain prose.
    /// Sprint 56 (TSK-0278): Made internal for testability. Returns null when no
    /// JSON brackets found (caller should detect this as a parse failure).
    /// </summary>
    internal static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}
