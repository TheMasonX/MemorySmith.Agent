namespace Agent.Core;

/// <summary>
/// Describes how an item can be acquired by the agent.
/// Consumed by <see cref="Agent.Planning.Goals.GenericGatherGoal"/> (completion check)
/// and <see cref="Agent.Planning.HtnTaskLibrary"/> (action sequencing).
///
/// Lives in Agent.Core so it can be referenced by both Agent.Memory
/// (MemorySmithItemRegistry) and Agent.Planning (GenericGatherGoal, HtnTaskLibrary).
///
/// LegacyBlockIds (pre-1.13 block-ID remapping) is intentionally omitted from the
/// MVP. It will be added in Phase 5 alongside a MinecraftVersion configuration option.
/// (TSK-0010 council blocker — see Data/Pages/council/tsk-0010-design-council-20260616.md)
/// </summary>
public sealed record ItemSpec
{
    /// <summary>
    /// Inventory key to check for completion, e.g. "oak_log", "iron_ingot".
    /// For smelting items this is the PRODUCT key, not the raw ore.
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>Human-readable display name, e.g. "Iron Ingot".</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Block IDs to mine in order to acquire this item.
    /// For smelting items this lists the RAW ore blocks, not the smelted product.
    /// Example — iron_ingot: ["iron_ore", "deepslate_iron_ore"]
    /// </summary>
    public IReadOnlyList<string> SourceBlocks { get; init; } = [];

    /// <summary>
    /// True if mining SourceBlocks yields a raw material that must be smelted to
    /// produce ItemId. When true, <see cref="Agent.Planning.Goals.GenericGatherGoal.IsComplete"/>
    /// checks inventory for ItemId (the smelted product). When false it sums SourceBlocks.
    /// Full smelting-chain automation (FurnaceTool) is deferred to Phase 4b.
    /// </summary>
    public bool RequiresSmelting { get; init; }

    /// <summary>
    /// Minimum harvest-level tool required: 0=hand, 1=wood, 2=stone, 3=iron, 4=diamond.
    /// </summary>
    public int MinHarvestLevel { get; init; }
}
