namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Places one or more blocks at the bot's current facing position.
/// Created when the LLM returns intent="place" — distinct from "build" which
/// requires a blueprint and handles full structure construction.
///
/// Does NOT implement IItemSpecGoal — that would cause GatherGoalDecomposer
/// to hijack it and decompose into [CraftItem, GetStatus, MineBlock] instead
/// of [PlaceBlock]. The PlaceBlockGoalDecomposer must be registered BEFORE
/// GatherGoalDecomposer in the decomposer registry.
///
/// Sprint 54: initial implementation.
/// </summary>
public sealed class PlaceBlockGoal : IGoal
{
    private readonly string _item;
    private readonly int _count;
    private int _dispatched;

    public PlaceBlockGoal(string item, int count = 1, int? x = null, int? y = null, int? z = null)
    {
        _item = item;
        _count = Math.Max(1, count);
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }
    public string Name => $"PlaceBlock:{_item}";
    public string Description => $"Place {_count}x {_item} in front";
    public string[] Phases => ["place"];
    public string? FailureReason { get; set; }

    /// <summary>Item being placed (e.g. "cobblestone", "torch").</summary>
    public string Item => _item;

    /// <summary>Number of blocks to place.</summary>
    public int Count => _count;

    /// <summary>Called by the decomposer when a place action is produced.</summary>
    public int Dispatched { get => _dispatched; set => _dispatched = value; }

    public bool IsComplete(WorldState state)
    {
        // Complete when all blocks have been dispatched as place actions.
        return _dispatched >= _count;
    }

    public bool HasFailed(WorldState state)
    {
        var have = state.Inventory.GetValueOrDefault(_item);
        return have <= 0 && !state.IsInventoryStale;
    }
}
