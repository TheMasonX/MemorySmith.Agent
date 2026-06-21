namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Decomposes gather-type goals (<see cref="GatherWoodGoal"/>, <see cref="IItemSpecGoal"/>).
/// Adapts each goal variant to <see cref="HtnTaskLibrary.DecomposeGatherItem"/>.
///
/// Sprint 26 P0-B: <see cref="IItemSpecGoal"/> catch-all arm now passes
/// <c>isg.TargetCount</c> instead of <c>Array.Empty&lt;string&gt;()</c>.
/// Previously any <see cref="IItemSpecGoal"/> implementor that wasn't
/// <see cref="GenericGatherGoal"/> or <see cref="GatherWoodGoal"/> would silently
/// receive the library's default count (10) instead of the requested quantity.
/// This is now safe because <see cref="IItemSpecGoal"/> declares
/// <c>int TargetCount =&gt; 1;</c> as a default interface method (DIM), so all
/// implementors provide a count.
///
/// Sprint 27 P0-D: the redundant <see cref="GenericGatherGoal"/> arm has been removed.
/// Since <see cref="GenericGatherGoal"/> implements <see cref="IItemSpecGoal"/>, the
/// <see cref="IItemSpecGoal"/> arm catches it correctly via class-member-wins-over-DIM
/// semantics. The <see cref="CanHandle"/> predicate is simplified accordingly.
/// </summary>
public sealed class GatherGoalDecomposer(HtnTaskLibrary taskLibrary) : IGoalDecomposer
{
    /// <summary>
    /// ItemSpec for oak logs, used as the fallback for legacy <see cref="GatherWoodGoal"/>.
    /// Mirrors the private OakLogSpec in <see cref="HtnTaskLibrary"/>.
    /// </summary>
    private static readonly ItemSpec OakLogSpec = new()
    {
        ItemId          = "oak_log",
        DisplayName     = "Oak Log",
        SourceBlocks    = ["oak_log", "birch_log", "spruce_log",
                           "dark_oak_log", "jungle_log", "acacia_log", "cherry_log"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    public bool CanHandle(IGoal goal) =>
        goal is GatherWoodGoal or IItemSpecGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var (spec, parameters) = goal switch
        {
            GatherWoodGoal g     => (OakLogSpec, new[] { g.TargetCount.ToString() }),
            // Sprint 26 P0-B: IItemSpecGoal now has TargetCount as a DIM (default = 1).
            // Catches GenericGatherGoal (class member wins over DIM), StubItemSpecGoal in
            // tests, and any future IItemSpecGoal implementor.
            IItemSpecGoal isg    => (isg.Spec, new[] { isg.TargetCount.ToString() }),
            _                     => throw new InvalidOperationException(
                $"GatherGoalDecomposer cannot handle {goal.GetType().Name}")
        };

        var actions = taskLibrary.DecomposeGatherItem(spec, parameters, state);
        return new ActionPlan(goal.Name, goal.Phases, actions);
    }
}
