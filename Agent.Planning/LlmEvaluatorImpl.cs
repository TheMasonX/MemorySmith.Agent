namespace Agent.Planning;

using Agent.Core;
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
    public async Task<bool> EvaluateAsync(
        IGoal goal,
        IReadOnlyList<ActionOutcome> outcomes,
        WorldState worldState,
        CancellationToken ct = default)
    {
        // Fast-path 1: too few data points to make a reliable judgement.
        if (outcomes.Count < MinOutcomesBeforeEval) return false;

        // Fast-path 2: all outcomes succeeded — no reason to replan.
        var failureCount = outcomes.Count(static o => !o.Success);
        if (failureCount == 0) return false;

        // Fast-path 3: provider offline — skip silently.
        if (!_provider.IsAvailable)
        {
            _logger.LogDebug(
                "[evaluator] provider '{Provider}' unavailable — skipping evaluation for goal {Goal}",
                _provider.ProviderName, goal.Name);
            return false;
        }

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userMessage  = BuildUserMessage(goal, outcomes, worldState);

            var raw = await _provider.CompleteAsync(systemPrompt, userMessage, ct);

            if (raw is null)
            {
                _logger.LogWarning(
                    "[evaluator] provider returned null for goal {Goal} — defaulting to no-replan",
                    goal.Name);
                return false;
            }

            var shouldReplan = ParseReplanDecision(raw);

            if (shouldReplan)
                _logger.LogInformation(
                    "[evaluator] recommends replan for goal {Goal} — {Count} outcomes, {Failures} failures",
                    goal.Name, outcomes.Count, failureCount);
            else
                _logger.LogDebug(
                    "[evaluator] says continue for goal {Goal} — {Count} outcomes, {Failures} failures",
                    goal.Name, outcomes.Count, failureCount);

            return shouldReplan;
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
            return false;
        }
    }

    // ── Prompt construction ───────────────────────────────────────────────────

    private static string BuildSystemPrompt() =>
        "You are an autonomous Minecraft agent evaluator. Decide whether the agent's " +
        "current plan is failing and a new plan should be requested.\n\n" +
        "Respond ONLY with compact JSON:\n" +
        "  { \"replan\": true }                     — abandon remaining actions, request fresh plan\n" +
        "  { \"replan\": false, \"reason\": \"...\" }  — continue executing the current plan\n\n" +
        "Recommend replan (true) when: multiple consecutive failures on the same tool, " +
        "a required resource that clearly does not exist, or a goal that cannot be completed " +
        "given the current world state.\n" +
        "Do NOT replan for a single transient failure or minor setbacks.\n" +
        "Keep \"reason\" under 15 words.";

    private static string BuildUserMessage(
        IGoal goal,
        IReadOnlyList<ActionOutcome> outcomes,
        WorldState worldState)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Goal: {goal.Name}");
        sb.AppendLine(
            $"World: HP={worldState.Health}/20, Food={worldState.Food}/20, " +
            $"Pos=({worldState.Position.X},{worldState.Position.Y},{worldState.Position.Z}), " +
            $"Inventory={worldState.Inventory.Count} distinct items");
        sb.AppendLine();
        sb.AppendLine("Recent outcomes (oldest first):");
        foreach (var o in outcomes.TakeLast(10))
            sb.AppendLine($"  [{(o.Success ? "OK  " : "FAIL")}] {o.ToolName}: {o.ObservationSummary}");
        return sb.ToString();
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    private static bool ParseReplanDecision(string response)
    {
        try
        {
            var json = ExtractJson(response);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("replan", out var p) && p.GetBoolean();
        }
        catch
        {
            return false; // unparseable → conservative no-replan
        }
    }

    /// <summary>Extracts the first JSON object from an LLM response that may contain prose.</summary>
    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }
}
