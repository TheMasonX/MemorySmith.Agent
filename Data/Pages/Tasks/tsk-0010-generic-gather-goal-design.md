# TSK-0010: GenericGatherGoal + ItemRegistry — Design Document

**Status:** Design — pending council review before implementation  
**Author:** Agent session 2026-06-16  
**Depends on:** TSK-0001..TSK-0009 (Phase 3 complete), Candidate 3–5 refactors  

---

## Problem Statement

`GatherWoodGoal` is hardcoded at every layer:
- **Goal** (`GatherWoodGoal.IsComplete`): sums `*_log` inventory keys — hardcoded suffix.
- **Planner** (`HtnTaskLibrary.GatherWoodDecompose`): uses `"minecraft:oak_log"` and `"minecraft:birch_log"` as the block IDs to mine — hardcoded.
- **Node.js mine loop**: block search uses `minecraft:oak_log` — hardcoded.

This cannot handle iron ore, diamonds, TConstruct cobalt, or any item requiring a smelting chain (you can't gather iron ingots directly — you mine iron ore and smelt). The agent also cannot learn about modded items at compile time.

---

## Design Goals

1. **Backward compatible**: `GatherWoodGoal` continues to work as a factory convenience. Existing tests pass unchanged.
2. **Vanilla-first**: Common vanilla items (oak log, stone, iron ore, diamond) work without any LLM call for the happy path.
3. **Mod-extensible**: Modded items (TConstruct cobalt, Thermal tin) are supported by populating a MemorySmith wiki page — no code change required.
4. **Smelting-aware**: `IsComplete` for `iron_ingot` checks the correct inventory key after smelting, not the raw ore.
5. **LLM as fallback, not primary**: Per D-003, LLM is invoked only when the deterministic registry lookup fails.

---

## Proposed Architecture

### 1. ItemSpec (data model)

```csharp
/// <summary>
/// Describes how an item can be acquired. Consumed by GenericGatherGoal and HtnTaskLibrary.
/// </summary>
public sealed record ItemSpec
{
    /// <summary>The inventory key to check for completion, e.g. "oak_log", "iron_ingot".</summary>
    public required string ItemId { get; init; }

    /// <summary>Human-readable display name, e.g. "Iron Ingot".</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The block(s) to mine to acquire this item. For items that require smelting,
    /// this is the RAW ore block, not the product.
    /// e.g. "iron_ingot" → SourceBlocks = ["iron_ore", "deepslate_iron_ore"]
    /// </summary>
    public IReadOnlyList<string> SourceBlocks { get; init; } = [];

    /// <summary>
    /// True if this item requires smelting of SourceBlocks to produce ItemId.
    /// When true, IsComplete checks inventory for ItemId (the smelted product).
    /// When false, IsComplete sums up SourceBlocks in inventory.
    /// </summary>
    public bool RequiresSmelting { get; init; }

    /// <summary>Optional: minimum harvest-level tool required (0=hand, 1=wood, 2=stone, etc.).</summary>
    public int MinHarvestLevel { get; init; }

    /// <summary>Optional: Minecraft-version-aware block ID overrides (pre-1.13 flattening).</summary>
    public IReadOnlyDictionary<string, string> LegacyBlockIds { get; init; }
        = new Dictionary<string, string>();
}
```

### 2. ItemRegistry (MemorySmith-backed lookup)

The ItemRegistry is NOT a code-level dictionary. It is a set of MemorySmith wiki pages at path `item-registry/{itemId}`.

**Wiki page format** (`item-registry/iron-ingot`):
```markdown
# iron-ingot

display_name: Iron Ingot
source_blocks: iron_ore, deepslate_iron_ore
requires_smelting: true
min_harvest_level: 2
```

**Why wiki pages?**
- Mod items are populated at runtime by the user or the LLM (no code deployment needed).
- The agent can search for `"item-registry/cobalt"` using its existing `SearchMemory` tool.
- Versioned, editable from the MemorySmith UI.

**`IItemRegistry` interface:**
```csharp
public interface IItemRegistry
{
    /// <summary>
    /// Looks up an ItemSpec by item ID.
    /// Returns null if not found (caller falls back to LLM).
    /// </summary>
    Task<ItemSpec?> GetAsync(string itemId, CancellationToken ct = default);
}
```

**`MemorySmithItemRegistry` implementation:**
```csharp
public sealed class MemorySmithItemRegistry(IMemoryGateway memory) : IItemRegistry
{
    public async Task<ItemSpec?> GetAsync(string itemId, CancellationToken ct = default)
    {
        var results = await memory.SearchAsync($"item-registry/{itemId}", ct);
        var page = results.FirstOrDefault(r => r.Slug?.Contains("item-registry") == true);
        if (page is null) return null;
        return ParseItemSpec(page.Content); // parses the markdown front-matter
    }
}
```

### 3. GenericGatherGoal

```csharp
/// <summary>
/// Gathers a target number of units of any item specified by an ItemSpec.
/// Replaces the hardcoded GatherWoodGoal for all vanilla and modded gather tasks.
/// </summary>
public sealed class GenericGatherGoal(ItemSpec item, int targetCount) : IGoal
{
    public string Name => $"Gather:{item.ItemId}";
    public string Description =>
        $"Gather at least {targetCount} {item.DisplayName}.";
    public string[] Phases => ["FindSource", "Mine", "Collect"];

    public bool IsComplete(WorldState state)
    {
        if (item.RequiresSmelting)
        {
            // Check for the smelted product in inventory
            return state.Inventory.GetValueOrDefault(item.ItemId) >= targetCount;
        }
        else
        {
            // Sum all matching source blocks (handles multi-block items like oak_log/birch_log)
            int total = 0;
            foreach (var block in item.SourceBlocks)
            {
                var shortKey = block.Contains(':') ? block.Split(':')[1] : block;
                total += state.Inventory.GetValueOrDefault(shortKey);
            }
            // Also count the item itself (in case it was acquired directly)
            total += state.Inventory.GetValueOrDefault(item.ItemId);
            return total >= targetCount;
        }
    }

    public bool HasFailed(WorldState state) =>
        state.Facts.TryGetValue($"goal:{Name}:failed", out var v) && v is true;
}
```

### 4. GoalFactory integration

`GoalFactory` gains an `IItemRegistry` dependency. The `"GatherWood"` registration becomes:

```csharp
["GatherWood"] = async (params, ct) =>
{
    var spec = await registry.GetAsync("oak_log", ct)
               ?? ItemSpec.ForWood(); // built-in fallback
    int count = ParseCount(params) ?? 10;
    return new GenericGatherGoal(spec, count);
}
```

User-defined goals like `"GatherIron"`, `"GatherCobalt"` work the same way — they just need a wiki page.

### 5. HtnTaskLibrary changes

`GatherWoodDecompose` is renamed `GatherItemDecompose` and accepts an `itemSpec` parameter:

```csharp
private static IReadOnlyList<ActionData> GatherItemDecompose(
    string itemId, string[] sourceBlocks, string[] parameters, WorldState state)
{
    var count = parameters.Length > 0 && int.TryParse(parameters[0], out var c) ? c : 10;
    var searchQuery = $"{itemId} location nearby source";

    var actions = new List<ActionData>
    {
        MakeAction("SearchMemory", ("query", searchQuery)),
        MakeAction("Wander", ("radius", (object?)40), ("maxDistanceFromSpawn", (object?)200)),
    };

    // Mine each source block type
    foreach (var block in sourceBlocks.Take(2)) // max 2 variants per plan cycle
        actions.Add(MakeAction("MineBlock", ("block", block), ("count", (object?)count)));

    actions.Add(MakeAction("GetStatus"));
    return actions;
}
```

**Open question (for council):** Should HtnTaskLibrary know about ItemSpec at compile time, or should it receive the list of source blocks as ActionData arguments? The second option avoids adding `Agent.Planning` → `Agent.Core`'s ItemSpec dependency.

### 6. Smelting-chain support (deferred, flagged for design)

For items requiring smelting:
- After mining iron ore, a `SmeltItems(ore, count)` action is needed.
- This requires a `FurnaceTool` (not yet implemented).
- `GenericGatherGoal.IsComplete` already handles the smelted-product check correctly.
- For Phase 4, `SmeltItems` is a deferred action sequence added to the task decomposer.

**Proposal**: For the first implementation, `RequiresSmelting = true` items are gatherable only if the smelted product is already in inventory (meaning a human smelted it or a previous session did). This is "IsComplete handles smelting correctly" but the bot does not DRIVE the smelting. Full smelting chain is Phase 5.

---

## Open Questions for Council

1. **ItemSpec in which project?** `ItemSpec` depends on nothing. It could live in `Agent.Core` (reusable by planning and memory layers) or `Agent.Planning` (closer to where goals live). Recommend: `Agent.Core`.

2. **Parsing wiki pages**: `MemorySmithItemRegistry.ParseItemSpec` reads markdown. Should this be a rigid YAML-like front-matter block, or free-form markdown with semantic search? Front-matter is parseable without LLM; free-form requires LLM to extract fields. Recommend: front-matter for vanilla items; LLM extraction as fallback for mod items where the page text is author-written.

3. **HtnTaskLibrary + ItemSpec coupling**: Does `HtnTaskLibrary` need to know about `ItemSpec`, or should it just receive `(blockIds: string[], count: int)` as task parameters? Decoupled approach keeps `Agent.Planning` from importing `Agent.Core.ItemSpec` (though `Agent.Planning` already imports `Agent.Core` for `WorldState` and `ActionData`, so the import is fine). Recommend: pass `ItemSpec` directly since the coupling already exists.

4. **`IsComplete` for multi-source items**: If an item has 3 source block variants (oak, birch, spruce logs), `IsComplete` currently sums all variants. But `targetCount = 10` might be satisfied by 4 oak + 3 birch + 3 spruce. Is that correct? Yes — the user asked for "10 logs", not "10 oak logs". This is the correct behaviour.

5. **Mod wiki page authoring**: Who writes `item-registry/cobalt-ore`? Options: (a) user adds it manually via MemorySmith UI; (b) when the LLM is asked to "gather cobalt" and the registry has no entry, the LLM asks clarifying questions and then creates the wiki page via `CreatePage`. Option (b) is the Phase 4 target. For Phase 4 scope, option (a) is sufficient.

---

## Phased Implementation Plan

**Phase 4a (this session, after council approval):**
1. Add `ItemSpec` record to `Agent.Core`
2. Add `IItemRegistry` interface and `MemorySmithItemRegistry` implementation
3. Add `GenericGatherGoal` to `Agent.Planning`
4. Update `GoalFactory` to support `"GatherItem:{itemId}"` goal names
5. Add `GatherItemDecompose` to `HtnTaskLibrary`
6. Keep `GatherWoodGoal` as a backward-compatible factory wrapper
7. Seed vanilla wiki pages: `item-registry/oak-log`, `item-registry/iron-ore`, `item-registry/diamond`
8. Add `GenericGatherGoalTests` (12+ tests) and `ItemRegistryTests` (mock-gateway based)

**Phase 4b (future):**
- `FurnaceTool` for smelting chain
- LLM-driven `CreatePage` for unknown mod items
- Node.js mine loop reading block variants from the ItemSpec action arguments

---

## Files to Create / Modify

| File | Action |
|---|---|
| `Agent.Core/ItemSpec.cs` | New |
| `Agent.Core/Interfaces/IItemRegistry.cs` | New |
| `Agent.Memory/MemorySmithItemRegistry.cs` | New |
| `Agent.Planning/Goals/GenericGatherGoal.cs` | New |
| `Agent.Planning/HtnTaskLibrary.cs` | Add GatherItemDecompose |
| `Agent.Planning/GoalFactory.cs` | Add GatherItem registration |
| `Data/Pages/item-registry/oak-log.md` | New seed page |
| `Data/Pages/item-registry/iron-ore.md` | New seed page |
| `Data/Pages/item-registry/diamond.md` | New seed page |
| `MemorySmith.Agent.Tests/GenericGatherGoalTests.cs` | New |
| `MemorySmith.Agent.Tests/ItemRegistryTests.cs` | New |
