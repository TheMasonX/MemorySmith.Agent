using Agent.Construction;
using Agent.Core;
using Agent.Planning;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Unit tests for <see cref="HtnTaskLibrary.DecomposeBuild"/> Sprint 2b:
/// the crafting chain that emits CraftItem actions after raw material gathering.
///
/// Tests call DecomposeBuild directly (it is public) to isolate the crafting chain
/// logic from HtnPlanner's surrounding phases and goal lifecycle.
/// </summary>
[TestFixture]
[Description("HtnTaskLibrary.DecomposeBuild: Sprint 2b crafting chain")]
public sealed class HtnTaskLibraryCraftingTests
{
    private static readonly IReadOnlyList<PlacementBlock> SingleBlock =
        [new PlacementBlock(0, 0, 0, "oak_planks")];

    private static HtnTaskLibrary Library() => new();

    private static Blueprint MakeBlueprint(params MaterialEntry[] materials) => new()
    {
        Id   = "test",
        Name = "Test Build",
        Materials = materials,
    };

    // ── oak_planks crafting ───────────────────────────────────────────────────

    [Test]
    public void DecomposeBuild_BlueprintHasPlanks_EmitsCraftItemPlanks()
    {
        var bp      = MakeBlueprint(new MaterialEntry("oak_planks", 32));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        var craft = actions.Where(a => ToolIs(a, "CraftItem")).ToList();
        Assert.That(craft, Has.Some.Matches<ActionData>(a =>
            ArgIs(a, "item", "oak_planks")),
            "CraftItem(oak_planks) should be emitted when blueprint needs planks.");
    }

    [Test]
    public void DecomposeBuild_BlueprintHasPlanks_InventorySufficient_SkipsCraft()
    {
        var bp    = MakeBlueprint(new MaterialEntry("oak_planks", 16));
        var state = new WorldState().With(b => b.AddInventoryItem("oak_planks", 16));

        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, state);

        Assert.That(
            actions.Where(a => ToolIs(a, "CraftItem") && ArgIs(a, "item", "oak_planks")),
            Is.Empty,
            "CraftItem(oak_planks) should be skipped when inventory is sufficient.");
    }

    [Test]
    public void DecomposeBuild_BlueprintHasPlanks_PartialInventory_CraftsDeficit()
    {
        var bp    = MakeBlueprint(new MaterialEntry("oak_planks", 20));
        var state = new WorldState().With(b => b.AddInventoryItem("oak_planks", 12));

        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, state);
        var craft   = actions.FirstOrDefault(a => ToolIs(a, "CraftItem") && ArgIs(a, "item", "oak_planks"));

        Assert.That(craft, Is.Not.Null, "Should emit CraftItem(oak_planks) for the deficit.");
        Assert.That(craft!.Arguments["count"], Is.EqualTo(8),
            "Should craft only the deficit (20 - 12 = 8).");
    }

    // ── crafting_table step ───────────────────────────────────────────────────

    [Test]
    public void DecomposeBuild_BlueprintHasCraftingTable_EmitsCraftTable()
    {
        var bp      = MakeBlueprint(new MaterialEntry("crafting_table", 1));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        Assert.That(
            actions.Where(a => ToolIs(a, "CraftItem") && ArgIs(a, "item", "crafting_table")),
            Is.Not.Empty,
            "CraftItem(crafting_table) should be emitted when blueprint needs a crafting table.");
    }

    [Test]
    public void DecomposeBuild_BlueprintHasOakSlab_EmitsCraftTableAndSlab()
    {
        // oak_slab requires a crafting table (3x1 recipe); the chain must emit
        // crafting_table before oak_slab even when crafting_table is not in blueprint Materials.
        var bp      = MakeBlueprint(new MaterialEntry("oak_slab", 12));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        var craftItems = actions.Where(a => ToolIs(a, "CraftItem"))
                                .Select(a => a.Arguments["item"]?.ToString())
                                .ToList();

        // B1: crafting_table must be auto-emitted even though it's not in blueprint Materials
        Assert.That(craftItems, Contains.Item("crafting_table"),
            "CraftItem(crafting_table) must be auto-emitted when oak_slab is needed (B1 fix).");
        Assert.That(craftItems, Contains.Item("oak_slab"),
            "CraftItem(oak_slab) should be emitted.");
        Assert.That(craftItems.IndexOf("crafting_table"),
            Is.LessThan(craftItems.IndexOf("oak_slab")),
            "crafting_table must be crafted before oak_slab (dependency order).");
    }

    /// <summary>
    /// B1 regression test: blueprint with chest but no explicit crafting_table Material
    /// must still auto-emit CraftItem(crafting_table) because chest is a 3x3 recipe.
    /// </summary>
    [Test]
    public void DecomposeBuild_BlueprintHasChest_NoExplicitTable_AutoEmitsCraftTable()
    {
        var bp      = MakeBlueprint(new MaterialEntry("chest", 2));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        var craftItems = actions.Where(a => ToolIs(a, "CraftItem"))
                                .Select(a => a.Arguments["item"]?.ToString())
                                .ToList();

        Assert.That(craftItems, Contains.Item("crafting_table"),
            "CraftItem(crafting_table) must be auto-emitted when chest is needed (B1 fix).");
        Assert.That(craftItems.IndexOf("crafting_table"),
            Is.LessThan(craftItems.IndexOf("chest")),
            "crafting_table must appear before chest in the crafting chain.");
    }

    [Test]
    public void DecomposeBuild_BlueprintHasChest_TableAlreadyInInventory_SkipsTableCraft()
    {
        var bp    = MakeBlueprint(new MaterialEntry("chest", 2));
        var state = new WorldState().With(b => b.AddInventoryItem("crafting_table", 1));

        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, state);

        Assert.That(
            actions.Where(a => ToolIs(a, "CraftItem") && ArgIs(a, "item", "crafting_table")),
            Is.Empty,
            "CraftItem(crafting_table) should be skipped when one is already in inventory.");
    }

    // ── torch chain (sticks + coal) ───────────────────────────────────────────

    [Test]
    public void DecomposeBuild_BlueprintHasTorch_EmitsCraftStickThenTorch()
    {
        var bp      = MakeBlueprint(new MaterialEntry("torch", 8));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        var craftItems = actions.Where(a => ToolIs(a, "CraftItem"))
                                .Select(a => a.Arguments["item"]?.ToString())
                                .ToList();

        Assert.That(craftItems, Contains.Item("stick"),
            "CraftItem(stick) should be emitted as an intermediate before CraftItem(torch).");
        Assert.That(craftItems, Contains.Item("torch"),
            "CraftItem(torch) should be emitted when blueprint needs torches.");
        Assert.That(craftItems.IndexOf("stick"),
            Is.LessThan(craftItems.IndexOf("torch")),
            "Sticks must be crafted before torches (dependency order).");
    }

    [Test]
    public void DecomposeBuild_BlueprintHasTorch_EmitsMineCoal_BeforeCrafting()
    {
        // When torch is in the blueprint and coal is not in inventory, a
        // MineBlock("coal_ore") action must be emitted during the gather phase.
        var bp      = MakeBlueprint(new MaterialEntry("torch", 4));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        var mineCoal = actions.FirstOrDefault(a =>
            ToolIs(a, "MineBlock") && ArgIs(a, "block", "coal_ore"));

        Assert.That(mineCoal, Is.Not.Null,
            "MineBlock(coal_ore) must be emitted when torch is needed and coal is missing.");
    }

    [Test]
    public void DecomposeBuild_BlueprintHasTorch_SufficientCoal_SkipsMineCoal()
    {
        // 4 torches = 1 craft batch = 1 coal needed; inventory already has 1 coal.
        var bp    = MakeBlueprint(new MaterialEntry("torch", 4));
        var state = new WorldState().With(b => b.AddInventoryItem("coal", 1));

        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, state);

        Assert.That(
            actions.Where(a => ToolIs(a, "MineBlock") && ArgIs(a, "block", "coal_ore")),
            Is.Empty,
            "MineBlock(coal_ore) should be skipped when inventory already has enough coal.");
    }

    // ── Non-mineable blocks are never mined ──────────────────────────────────

    [Test]
    public void DecomposeBuild_BlueprintHasPlanks_NoMineBlockForPlanks()
    {
        var bp      = MakeBlueprint(new MaterialEntry("oak_planks", 64));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        Assert.That(
            actions.Where(a => ToolIs(a, "MineBlock") && ArgIs(a, "block", "oak_planks")),
            Is.Empty,
            "oak_planks is crafted, not mined — DecomposeBuild must never emit MineBlock(oak_planks).");
    }

    // ── Plan structure: still ends with GetStatus ─────────────────────────────

    [Test]
    public void DecomposeBuild_WithCraftingItems_StillEndsWithGetStatus()
    {
        var bp      = MakeBlueprint(new MaterialEntry("oak_planks", 4), new MaterialEntry("torch", 4));
        var actions = Library().DecomposeBuild(bp, SingleBlock, 0, 0, 0, new WorldState());

        Assert.That(actions.Last().Tool, Is.EqualTo("GetStatus"),
            "Plan must end with GetStatus regardless of crafting chain.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool ToolIs(ActionData a, string tool) =>
        string.Equals(a.Tool, tool, StringComparison.OrdinalIgnoreCase);

    private static bool ArgIs(ActionData a, string key, string value) =>
        a.Arguments.TryGetValue(key, out var v) &&
        string.Equals(v?.ToString(), value, StringComparison.OrdinalIgnoreCase);
}
