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

        // Use goal coordinates if provided, otherwise place at bot's current position
        var targetX = pg.X ?? state.Position.X;
        var targetY = pg.Y ?? state.Position.Y;
        var targetZ = pg.Z ?? state.Position.Z;

        // Place the requested number of blocks
        for (int i = 0; i < pg.Count; i++)
        {
            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["x"] = targetX,
                ["y"] = targetY,
                ["z"] = targetZ,
                ["block"] = pg.Item,
                ["count"] = 1,
            };
            actions.Add(new ActionData
            {
                Tool = "place",
                Arguments = args,
            });
        }

        // Sprint 58 (TSK-0317): Dispatched is now incremented per confirmed
        // BlockPlacedEvent in AgentBackgroundService, not pre-set here.
        // This prevents IsComplete from returning true before blocks are placed.
        actions.Add(new ActionData { Tool = "GetStatus" });
        return new ActionPlan(goal.Name, goal.Phases, actions);
    }
}
