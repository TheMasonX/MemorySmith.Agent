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
    /// <returns>
    /// <see langword="true"/> to trigger replanning (agent discards remaining actions and
    /// calls <see cref="IPlanner.PlanAsync"/> again); <see langword="false"/> to continue.
    /// </returns>
    Task<bool> EvaluateAsync(IGoal goal, IReadOnlyList<ActionOutcome> outcomes,
        WorldState worldState, CancellationToken ct = default);
}
