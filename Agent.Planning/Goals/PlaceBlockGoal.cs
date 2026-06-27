namespace Agent.Planning.Goals;

using Agent.Core;

/// <summary>
/// Places one or more blocks at a target position (or in front of the bot).
/// Created when the LLM returns intent="place" — distinct from "build" which
/// requires a blueprint and handles full structure construction.
///
/// Sprint 54: initial implementation for single/multi block placement via chat.
/// </summary>
public sealed class PlaceBlockGoal : IGoal, IItemSpecGoal
{
    private readonly string _item;
    private readonly int _count;
    private readonly int? _x;
    private readonly int? _y;
    private readonly int? _z;

    public PlaceBlockGoal(string item, int count = 1, int? x = null, int? y = null, int? z = null)
    {
        _item = item;
        _count = Math.Max(1, count);
        _x = x;
        _y = y;
        _z = z;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }
    public string Name => $"PlaceBlock:{_item}";
    public string Description => _x is not null
        ? $"Place {_count}x {_item} at ({_x},{_y},{_z})"
        : $"Place {_count}x {_item} in front";
    public string[] Phases => ["place"];
    public string? FailureReason { get; set; }
    public ItemSpec Spec => new()
    {
        ItemId = _item,
        DisplayName = _item,
        SourceBlocks = [_item],
    };
    public int TargetCount => _count;

    public bool IsComplete(WorldState state)
    {
        // Complete when we've placed at least the requested count.
        // The fact "blocksPlaced" is incremented by BlockPlacedEvent handler.
        var placed = state.Facts.TryGetValue($"placeBlock:{_item}:placed", out var val)
            && val is string s && int.TryParse(s, out var p)
                ? p : 0;
        return placed >= _count;
    }

    public bool HasFailed(WorldState state)
    {
        // Fail if we don't have the block in inventory and can't get it.
        var have = state.Inventory.GetValueOrDefault(_item);
        return have <= 0 && state.IsInventoryStale == false;
    }
}
