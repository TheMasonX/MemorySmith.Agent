namespace Agent.Core;

/// <summary>
/// Sprint 38 P3: Evaluates accumulated ActionOutcomes after a plan cycle and decides
/// whether the agent should replan based on observed world effects.
///
/// This is the first step toward observation-driven replanning:
///   Plan → Execute → ActionOutcome[] → ILlmEvaluator.EvaluateAsync → Replan?
///
/// Sprint 38 introduces the interface and wires a null stub in DispatchActionsAsync.
/// Sprint 39 will provide a concrete LLM-backed implementation.
///
/// Sprint 54 (TSK-0220): Replaced <c>Task&lt;bool&gt;</c> return with <see cref="EvaluationResult"/>
/// so the LLM can suggest a specific remediation action (skip block #N, step back, retry).
///
/// Sprint 55 (TSK-0155): Added <see cref="WorldStateDiff"/> parameter so the evaluator
/// can compare expected vs. actual outcomes across inventory, position, health, and entity
/// dimensions — closing the observe→predict→observe→evaluate feedback loop.
/// </summary>
public interface ILlmEvaluator
{
    /// <summary>
    /// Evaluates the outcomes of dispatched actions and decides whether the current
    /// plan should be abandoned and a new one requested.
    /// </summary>
    /// <param name="goal">The active goal being pursued.</param>
    /// <param name="outcomes">Outcomes accumulated since the last plan was generated.</param>
    /// <param name="worldState">
    /// Current world state at evaluation time. Allows the evaluator to cross-check outcomes
    /// against observed world changes (e.g. inventory delta, position change, health).
    /// Added in Sprint 39 D-S38-02.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="forceEvaluate">
    /// Sprint 54 (TSK-0222): When true, bypasses fast-path short-circuits (too-few-outcomes,
    /// all-succeeded) and always calls the LLM. Used by TryLlmReplanOnStallAsync when the
    /// governor has already declared a stall — the stall itself is evidence of failure,
    /// even if individual tool outcomes appear successful (e.g., fire-and-forget place actions).
    /// </param>
    /// <param name="diff">
    /// Sprint 55 (TSK-0155): Expected vs. actual world state comparison. Null when no
    /// pre-action snapshot was available (legacy path). Provides structured mismatch
    /// data so the LLM can reason about inventory shortfalls, position drift, health
    /// drops, and entity threats without parsing free-form outcome summaries.
    /// </param>
    /// <returns>
    /// <see cref="EvaluationResult"/> with <see cref="EvaluationResult.ShouldReplan"/> set to
    /// <see langword="true"/> to trigger replanning and an optional <see cref="EvaluationResult.Suggestion"/>
    /// for specific remediation.
    /// </returns>
    Task<EvaluationResult> EvaluateAsync(IGoal goal, IReadOnlyList<ActionOutcome> outcomes,
        WorldState worldState, CancellationToken ct = default, bool forceEvaluate = false,
        WorldStateDiff? diff = null);
}

/// <summary>
/// Sprint 54 (TSK-0220): Result of LLM evaluation with optional remediation suggestion.
/// Sprint 56 (TSK-0278): Added IsSuccess and FailureReason for structured parse-failure signaling.
/// </summary>
/// <param name="ShouldReplan">True to abandon remaining actions and request a fresh plan.</param>
/// <param name="Reason">Short explanation of the recommendation.</param>
/// <param name="Suggestion">
/// Optional specific remediation action the LLM recommends.
/// Examples: "skip block #9 (occupiedBy_stone)", "step back 3 blocks and retry block #187",
/// "clear plan and move to origin before rebuilding".
/// </param>
public sealed record EvaluationResult(bool ShouldReplan, string Reason = "", string Suggestion = "")
{
    /// <summary>
    /// Whether the evaluation itself succeeded (LLM responded, parse was valid).
    /// When false, <see cref="FailureReason"/> contains the specific failure mode.
    /// Defaults to true for backward compatibility with existing call sites.
    /// </summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>
    /// When <see cref="IsSuccess"/> is false, the specific failure mode.
    /// Values: "ParseFailure", "Timeout", "NullResponse", "ProviderUnavailable".
    /// </summary>
    public string? FailureReason { get; init; }
}
