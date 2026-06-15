namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Gather a target number of wood logs from nearby trees.
///
/// Phases: FindTree → MineWood → Collect
///
/// IsComplete: inventory contains at least targetCount logs
///   (any *_log variant counts).
/// HasFailed: world state has set "goal:GatherWood:failed" = true
///   (set by AgentBackgroundService after 3 consecutive failed tool calls).
/// </summary>
public sealed class GatherWoodGoal(int targetCount = 10) : IGoal
{
    public string Name => "GatherWood";
    public string Description => $"Gather at least {targetCount} wood logs from nearby trees.";
    public string[] Phases => ["FindTree", "MineWood", "Collect"];

    public bool IsComplete(WorldState state) =>
        GetWoodCount(state) >= targetCount;

    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue("goal:GatherWood:failed", out var v) && v is true;

    public int TargetCount => targetCount;

    private static int GetWoodCount(WorldState state) =>
        state.Inventory
            .Where(kv => kv.Key.EndsWith("_log", StringComparison.OrdinalIgnoreCase))
            .Sum(kv => kv.Value);
}
