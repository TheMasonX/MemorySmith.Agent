namespace Agent.Core;

/// <summary>
/// A high-level agent objective. Goals are evaluated each reasoning cycle.
/// When IsComplete returns true the agent idles; when HasFailed returns true
/// the planner is asked to replan or the user is alerted.
/// </summary>
public interface IGoal
{
    string Name { get; }
    string Description { get; }
    string[] Phases { get; }

    /// <summary>
    /// Nullable failure reason. <see langword="null"/> means the goal hasn't
    /// failed yet or no reason was recorded. Set by the agent service when
    /// <see cref="HasFailed"/> returns <see langword="true"/>.
    /// </summary>
    string? FailureReason { get; set; }

    /// <summary>
    /// Optional per-goal override for the damage interrupt threshold (in HP).
    /// <para>
    /// <see langword="null"/> (default) means use the system-wide default of 6 HP —
    /// the agent interrupts the current plan and re-evaluates when health drops by
    /// this amount or more in a single damage event.
    /// </para>
    /// <para>
    /// <c>0</c> means never interrupt this goal on damage. Reserved for future combat
    /// goals where the goal itself manages damage response and an interrupt would
    /// abort the combat plan mid-swing.
    /// </para>
    /// <para>
    /// Any other positive value sets a goal-specific threshold (e.g., a fragile
    /// exploration goal might use 3 HP to bail out earlier).
    /// </para>
    /// <para>
    /// Added in Sprint 23 (B-2 resolution) to support context-aware damage interrupt
    /// policy without forcing every existing goal to opt in.
    /// </para>
    /// </summary>
    int? DamageInterruptThresholdHp => null;

    bool IsComplete(WorldState state);
    bool HasFailed(WorldState state);
}
