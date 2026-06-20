namespace Agent.Core;

/// <summary>
/// Marker interface for goals that are driven by an <see cref="ItemSpec"/>.
///
/// Allows the HTN planner to extract the <see cref="Spec"/> via a single interface
/// check rather than spreading <c>goal is ConcreteType</c> patterns across every
/// planner branch. A second ItemSpec-based goal (e.g. a crafting goal) can implement
/// this interface without requiring a new planner branch.
///
/// Introduced in TSK-0011 Phase 4b (deferred D2 from TSK-0010).
///
/// Sprint 26 P0-B: Added <see cref="TargetCount"/> as a default interface method.
/// Previously, callers had to cast to <see cref="GenericGatherGoal"/> to access the
/// count, causing <see cref="GatherGoalDecomposer"/> and <see cref="HtnPlanner"/> to
/// silently default to count=10 for any IItemSpecGoal that wasn't GenericGatherGoal.
/// Implementors that already expose <c>int TargetCount</c> satisfy this automatically;
/// new implementors that don't provide it receive the default of 1.
/// </summary>
public interface IItemSpecGoal : IGoal
{
    /// <summary>The item specification that drives this goal's completion and decomposition.</summary>
    ItemSpec Spec { get; }

    /// <summary>
    /// The number of items to acquire. Defaults to 1.
    /// <para>
    /// Implementors with an existing <c>int TargetCount</c> property satisfy this
    /// automatically (the class member takes priority over the default implementation).
    /// New implementors that don't override this receive 1, which is a safe conservative
    /// default (better than the DecomposeGatherItem library default of 10).
    /// </para>
    /// </summary>
    int TargetCount => 1;
}
