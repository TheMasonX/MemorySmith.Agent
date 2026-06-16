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
/// </summary>
public interface IItemSpecGoal : IGoal
{
    /// <summary>The item specification that drives this goal's completion and decomposition.</summary>
    ItemSpec Spec { get; }
}
