namespace Agent.Planning.Goals;

using Agent.Construction;
using Agent.Core;

/// <summary>
/// Marker interface for build goals. Replaces fragile <c>goal is BuildGoal</c>
/// type checks throughout the planner, decomposers, recovery paths, and governors.
/// </summary>
public interface IBuildGoal : IGoal
{
    Blueprint Blueprint { get; }
    IReadOnlyList<PlacementBlock> Blocks { get; }
    BuildOrigin? Origin { get; }
    bool HasExplicitOrigin { get; }
}
