namespace Agent.Planning;

using Agent.Construction;
using Agent.Core;
using Agent.Planning.Goals;

/// <summary>
/// Decomposes a BuildGoal into its construction phases.
/// Delegates to HtnTaskLibrary.DecomposeBuild with blueprint, blocks,
/// and origin coordinates read from world-state facts.
/// </summary>
public sealed class BuildGoalDecomposer(HtnTaskLibrary taskLibrary) : IGoalDecomposer
{
    public bool CanHandle(IGoal goal) => goal is BuildGoal;

    public ActionPlan Decompose(IGoal goal, WorldState state)
    {
        var bg = (BuildGoal)goal;

        var ox = ReadOriginFact(state, bg.Blueprint.Id, "x");
        var oy = ReadOriginFact(state, bg.Blueprint.Id, "y");
        var oz = ReadOriginFact(state, bg.Blueprint.Id, "z");

        var actions = taskLibrary.DecomposeBuild(
            bg.Blueprint, bg.Blocks, ox, oy, oz, state);

        return new ActionPlan(bg.Name, bg.Phases, actions);
    }

    /// <summary>
    /// Reads an integer build origin coordinate from world-state facts.
    /// Key format: "build:{blueprintId}:origin:{axis}".
    /// Returns 0 if the fact is absent or unparseable.
    /// </summary>
    private static int ReadOriginFact(WorldState state, string blueprintId, string axis)
    {
        var key = $"build:{blueprintId}:origin:{axis}";
        return state.Facts.TryGetValue(key, out var v)
            ? v switch
            {
                int i                                          => i,
                long l                                         => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _                                              => 0,
            }
            : 0;
    }
}
