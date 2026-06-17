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
        return goal switch
        {
            GatherWoodGoal g =>
            {
                var parameters = new[] { g.TargetCount.ToString() };
                var actions = taskLibrary.DecomposeGatherItem(
                    OakLogSpec, parameters, state);
                return new ActionPlan(g.Name, g.Phases, actions);
            },
            GenericGatherGoal gg =>
            {
                var actions = taskLibrary.DecomposeGatherItem(
                    gg.Spec, [], state);
                return new ActionPlan(gg.Name, gg.Phases, actions);
            },
            IItemSpecGoal isg =>
            {
                var actions = taskLibrary.DecomposeGatherItem(
                    isg.Spec, [], state);
                return new ActionPlan(isg.Name, isg.Phases, actions);
            },
            _ => throw new InvalidOperationException(
                $"GatherGoalDecomposer cannot handle {goal.GetType().Name}")
        };
    }
}
