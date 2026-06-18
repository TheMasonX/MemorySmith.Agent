namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Decomposes gather-type goals (GatherWood, GenericGatherGoal, IItemSpecGoal).
/// Adapts each goal variant to HtnTaskLibrary.DecomposeGatherItem.
/// </summary>
public sealed class GatherGoalDecomposer(HtnTaskLibrary taskLibrary) : IGoalDecomposer
{
    /// <summary>
    /// ItemSpec for oak logs, used as the fallback for legacy GatherWoodGoal.
    /// Mirrors the private OakLogSpec in HtnTaskLibrary.
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
        goal is GatherWoodGoal or GenericGatherGoal or IItemSpecGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var (spec, parameters) = goal switch
        {
            GatherWoodGoal g     => (OakLogSpec, new[] { g.TargetCount.ToString() }),
            // Sprint 18 fix: pass TargetCount so GatherItemDecompose uses the requested
            // quantity instead of always defaulting to 10 (the "get 1 dirt mines 10" bug).
            GenericGatherGoal gg => (gg.Spec, new[] { gg.TargetCount.ToString() }),
            IItemSpecGoal isg    => (isg.Spec, Array.Empty<string>()),
            _ => throw new InvalidOperationException(
                $"GatherGoalDecomposer cannot handle {goal.GetType().Name}")
        };

        var actions = taskLibrary.DecomposeGatherItem(spec, parameters, state);
        return new ActionPlan(goal.Name, goal.Phases, actions);
    }
}
