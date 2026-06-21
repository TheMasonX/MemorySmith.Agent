namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Decomposes a <see cref="CraftItemGoal"/> into the crafting action sequence
/// produced by <see cref="HtnTaskLibrary.DecomposeCraftItem"/>.
///
/// Sprint 27 P0-D: extracted from the hardcoded branch 4 in <see cref="HtnPlanner"/>
/// (Sprint 13). Registering this decomposer routes all CraftItemGoal planning
/// through <see cref="DecomposerRegistry"/> → <see cref="PlannerRouter"/> instead
/// of the type-switch inside <see cref="HtnPlanner"/>, making <see cref="HtnPlanner"/>
/// a pure phase-by-phase fallback with no goal-type knowledge.
/// </summary>
public sealed class CraftItemGoalDecomposer(HtnTaskLibrary taskLibrary) : IGoalDecomposer
{
    /// <inheritdoc />
    public bool CanHandle(IGoal goal) => goal is CraftItemGoal;

    /// <inheritdoc />
    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var cig = (CraftItemGoal)goal;
        var actions = taskLibrary.DecomposeCraftItem(cig.ItemId, cig.Count, state);
        return new ActionPlan(goal.Name, goal.Phases.ToArray(), actions);
    }
}
