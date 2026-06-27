namespace Agent.Planning;

using Agent.Core;
using Agent.Planning.Goals;
using System.Collections.Generic;

/// <summary>
/// Decomposes <see cref="PlaceBlockGoal"/> into individual <see cref="ActionData"/>
/// PlaceBlock dispatches. If target coordinates are provided, the bot navigates
/// there first; otherwise it places the block at its current facing position.
///
/// Sprint 54: initial implementation.
/// </summary>
public sealed class PlaceBlockGoalDecomposer : IGoalDecomposer
{
    public bool CanHandle(IGoal goal) => goal is PlaceBlockGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var pg = (PlaceBlockGoal)goal;
        var actions = new List<ActionData>();

        // Place the requested number of blocks
        for (int i = 0; i < pg.TargetCount; i++)
        {
            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["block"] = pg.Spec.ItemId,
                ["count"] = 1,
            };
            actions.Add(new ActionData
            {
                Tool = "place",
                Arguments = args,
            });
        }

        actions.Add(new ActionData { Tool = "GetStatus" });
        return new ActionPlan(goal.Name, goal.Phases, actions);
    }
}
