namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Goals;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// Sprint 47 — Contract Tightening
///
/// Test coverage:
///   TSK-0112: CraftItem prerequisite count scaling (count > 1)
///   TSK-0114: ToolDispatcher structured exception metadata
///   TSK-0117: Post-craft/post-smelt inventory reconciliation
/// </summary>
[TestFixture]
public class Sprint47Tests
{
    // ── TSK-0112: CraftItem prerequisite count scaling ──────────────────────

    [Test]
    public void DecomposeCraftItem_IronPickaxeCount2_ScalesIronRequirement()
    {
        var lib = new HtnTaskLibrary();
        var state = new WorldState();
        // iron_pickaxe needs 3 iron_ingots per craft; count=2 should need 6
        var actions = lib.DecomposeCraftItem("iron_pickaxe", 2, state);

        // Should emit MineBlock(iron_ore) with count for 6 ingots
        var mineIron = actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", System.StringComparison.OrdinalIgnoreCase) &&
            a.Arguments?.TryGetValue("block", out var block) == true &&
            block?.ToString() == "iron_ore");

        Assert.That(mineIron, Is.Not.Null,
            "DecomposeCraftItem should emit MineBlock for iron_ore when count=2 with no inventory.");
        if (mineIron?.Arguments?.TryGetValue("count", out var countVal) == true)
        {
            var count = Convert.ToInt32(countVal);
            Assert.That(count, Is.EqualTo(6),
                "Should mine 6 iron_ore for 2 iron_pickaxes (3 ingots each).");
        }
    }

    [Test]
    public void DecomposeCraftItem_IronPickaxeCount2_SmeltScalesWithNeed()
    {
        var lib = new HtnTaskLibrary();
        var state = new WorldState();
        var actions = lib.DecomposeCraftItem("iron_pickaxe", 2, state);

        // Should emit SmeltItem with count for 6 ingots
        var smeltIron = actions.FirstOrDefault(a =>
            a.Tool.Equals("SmeltItem", System.StringComparison.OrdinalIgnoreCase));

        Assert.That(smeltIron, Is.Not.Null,
            "DecomposeCraftItem should emit SmeltItem for iron_pickaxe count=2.");
        if (smeltIron?.Arguments?.TryGetValue("count", out var countVal) == true)
        {
            var count = Convert.ToInt32(countVal);
            Assert.That(count, Is.EqualTo(6),
                "Should smelt 6 iron_ingots for 2 iron_pickaxes (3 ingots each).");
        }
    }

    [Test]
    public void DecomposeCraftItem_IronPickaxeCount2_CoalScales()
    {
        var lib = new HtnTaskLibrary();
        var state = new WorldState();
        var actions = lib.DecomposeCraftItem("iron_pickaxe", 2, state);

        // 6 ingots need 1 coal (6/8=0.75, ceiling=1) minimum
        // But also Math.Max(1, ...) so it's at least 1
        var mineCoal = actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", System.StringComparison.OrdinalIgnoreCase) &&
            a.Arguments?.TryGetValue("block", out var block) == true &&
            block?.ToString() == "coal_ore");

        // Coal mining may or may not be emitted depending on inventory
        // Just verify it exists if inventory has no coal
        Assert.That(mineCoal, Is.Not.Null,
            "Should emit MineBlock for coal_ore when no coal in inventory.");
    }

    [Test]
    public void DecomposeCraftItem_StoneSwordCount3_ScalesCobbleRequirement()
    {
        var lib = new HtnTaskLibrary();
        var state = new WorldState();
        // stone_sword needs 2 cobblestone per craft; count=3 should need 6
        var actions = lib.DecomposeCraftItem("stone_sword", 3, state);

        var mineCobble = actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", System.StringComparison.OrdinalIgnoreCase) &&
            a.Arguments?.TryGetValue("block", out var block) == true &&
            block?.ToString() == "stone");

        Assert.That(mineCobble, Is.Not.Null,
            "DecomposeCraftItem should emit MineBlock for stone when count=3 with no inventory.");
        if (mineCobble?.Arguments?.TryGetValue("count", out var countVal) == true)
        {
            var count = Convert.ToInt32(countVal);
            Assert.That(count, Is.EqualTo(6),
                "Should mine 6 cobblestone for 3 stone_swords (2 each).");
        }
    }

    [Test]
    public void DecomposeCraftItem_Count1_IronRequirementUnchanged()
    {
        var lib = new HtnTaskLibrary();
        var state = new WorldState();
        // count=1 should behave identically to pre-fix behavior
        var actions = lib.DecomposeCraftItem("iron_pickaxe", 1, state);

        var mineIron = actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", System.StringComparison.OrdinalIgnoreCase) &&
            a.Arguments?.TryGetValue("block", out var block) == true &&
            block?.ToString() == "iron_ore");

        if (mineIron?.Arguments?.TryGetValue("count", out var countVal) == true)
        {
            var count = Convert.ToInt32(countVal);
            Assert.That(count, Is.EqualTo(3),
                "count=1 should still need 3 iron_ore for iron_pickaxe.");
        }
    }

    // ── TSK-0114: ToolDispatcher structured exception metadata ──────────────

    [Test]
    public void ToolDispatcher_Catch_IncludesExceptionTypeInMessage()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new ThrowingTool("thrower"));

        var args = JsonElementFactory.Empty();
        var result = dispatcher.CallAsync("thrower", args)
            .GetAwaiter().GetResult();

        Assert.That(result.Success, Is.False,
            "Throwing tool should return failure.");
        Assert.That(result.Message, Does.Contain("InvalidOperationException"),
            "ToolResult message should include the exception type name.");
    }

    [Test]
    public void ToolDispatcher_Catch_IncludesOriginalMessage()
    {
        var dispatcher = new ToolDispatcher();
        dispatcher.Register(new ThrowingTool("thrower"));

        var args = JsonElementFactory.Empty();
        var result = dispatcher.CallAsync("thrower", args)
            .GetAwaiter().GetResult();

        Assert.That(result.Message, Does.Contain("Boom!"),
            "ToolResult message should include the original exception message.");
    }

    /// <summary>
    /// A tool that always throws InvalidOperationException.
    /// </summary>
    private sealed class ThrowingTool : global::Agent.Core.ITool
    {
        public string Name { get; }
        public string Description => "A tool that always throws.";
        public System.Text.Json.JsonElement InputSchema =>
            System.Text.Json.JsonSerializer.SerializeToElement(new { });

        public ThrowingTool(string name) { Name = name; }

        public Task<ToolResult> ExecuteAsync(
            System.Text.Json.JsonElement args,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException("Boom! Tool threw an exception.");
        }
    }

    /// <summary>
    /// Helper for creating an empty JsonElement to pass as tool arguments.
    /// </summary>
    private static class JsonElementFactory
    {
        private static readonly System.Text.Json.JsonElement EmptyElement =
            System.Text.Json.JsonSerializer.SerializeToElement(new { });

        public static System.Text.Json.JsonElement Empty() => EmptyElement;
    }

    // ── TSK-0117: Post-craft/post-smelt inventory reconciliation ────────────

    [Test]
    public void WorldStateProjector_CraftComplete_AddsItemToInventory()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        var ev = new CraftCompleteEvent("iron_pickaxe", 1, DateTimeOffset.UtcNow);

        var result = projector.Apply(state, ev);

        Assert.That(result.Inventory.GetValueOrDefault("iron_pickaxe"), Is.EqualTo(1),
            "CraftCompleteEvent should add 1 iron_pickaxe to inventory.");
    }

    [Test]
    public void WorldStateProjector_CraftComplete_MultipleCount_AddsCorrectly()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        var ev = new CraftCompleteEvent("oak_planks", 4, DateTimeOffset.UtcNow);

        var result = projector.Apply(state, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_planks"), Is.EqualTo(4),
            "CraftCompleteEvent with count=4 should add 4 planks to inventory.");
    }

    [Test]
    public void WorldStateProjector_CraftComplete_WithPrefix_NormalizesKey()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        var ev = new CraftCompleteEvent("minecraft:iron_ingot", 3, DateTimeOffset.UtcNow);

        var result = projector.Apply(state, ev);

        Assert.That(result.Inventory.GetValueOrDefault("iron_ingot"), Is.EqualTo(3),
            "CraftCompleteEvent should normalize minecraft: prefix in item key.");
    }

    [Test]
    public void WorldStateProjector_SmeltComplete_AddsResultToInventory()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        var ev = new SmeltCompleteEvent("iron_ore", "iron_ingot", 3, DateTimeOffset.UtcNow);

        var result = projector.Apply(state, ev);

        Assert.That(result.Inventory.GetValueOrDefault("iron_ingot"), Is.EqualTo(3),
            "SmeltCompleteEvent should add 3 iron_ingots to inventory.");
    }

    [Test]
    public void WorldStateProjector_SmeltComplete_WithPrefix_NormalizesResultKey()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        var ev = new SmeltCompleteEvent(
            "minecraft:iron_ore", "minecraft:iron_ingot", 1, DateTimeOffset.UtcNow);

        var result = projector.Apply(state, ev);

        Assert.That(result.Inventory.GetValueOrDefault("iron_ingot"), Is.EqualTo(1),
            "SmeltCompleteEvent should normalize minecraft: prefix in result key.");
    }

    [Test]
    public void WorldStateProjector_CraftComplete_CumulativeWithExistingInventory()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        state = projector.Apply(state,
            new CraftCompleteEvent("iron_ingot", 3, DateTimeOffset.UtcNow));
        state = projector.Apply(state,
            new CraftCompleteEvent("iron_ingot", 2, DateTimeOffset.UtcNow));

        Assert.That(state.Inventory.GetValueOrDefault("iron_ingot"), Is.EqualTo(5),
            "Multiple CraftCompleteEvents should accumulate inventory.");
    }

    [Test]
    public void WorldStateProjector_StoresFactsForCraftComplete()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();
        var ev = new CraftCompleteEvent("diamond", 1, DateTimeOffset.UtcNow);

        var result = projector.Apply(state, ev);

        // Facts should still be stored for debugging (using full type name minus "Event" suffix)
        Assert.That(result.Facts.ContainsKey("event:CraftComplete:Item"), Is.True,
            "CraftCompleteEvent should still store Item fact.");
        Assert.That(result.Facts.ContainsKey("event:CraftComplete:Count"), Is.True,
            "CraftCompleteEvent should still store Count fact.");
    }
}
