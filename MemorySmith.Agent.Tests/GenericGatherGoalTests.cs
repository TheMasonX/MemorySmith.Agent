using Agent.Core;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests for <see cref="GenericGatherGoal"/>.
///
/// Covers: IsComplete (non-smelting + smelting), multi-source summation,
/// namespace prefix stripping, HasFailed flag, Name / Description / Phases
/// shape, and boundary conditions (empty inventory, exact target count).
/// </summary>
[TestFixture]
public class GenericGatherGoalTests
{
    // ── Fixture helpers ───────────────────────────────────────────────────────

    private static ItemSpec OakLogSpec() => new()
    {
        ItemId           = "oak_log",
        DisplayName      = "Oak Log",
        SourceBlocks     = ["oak_log", "birch_log", "spruce_log"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    private static ItemSpec IronIngotSpec() => new()
    {
        ItemId           = "iron_ingot",
        DisplayName      = "Iron Ingot",
        SourceBlocks     = ["iron_ore", "deepslate_iron_ore"],
        RequiresSmelting = true,
        MinHarvestLevel  = 2,
    };

    private static ItemSpec DiamondSpec() => new()
    {
        ItemId           = "diamond",
        DisplayName      = "Diamond",
        SourceBlocks     = ["diamond_ore", "deepslate_diamond_ore"],
        RequiresSmelting = false,
        MinHarvestLevel  = 3,
    };

    private static ItemSpec NamespacedSpec() => new()
    {
        ItemId           = "oak_log",
        DisplayName      = "Oak Log",
        SourceBlocks     = ["minecraft:oak_log", "minecraft:birch_log"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    // ── Goal metadata ─────────────────────────────────────────────────────────

    [Test]
    public void Name_IncludesItemId()
    {
        var goal = new GenericGatherGoal(OakLogSpec(), 10);
        Assert.That(goal.Name, Is.EqualTo("Gather:oak_log"));
    }

    [Test]
    public void Name_IronIngot_IncludesItemId()
    {
        var goal = new GenericGatherGoal(IronIngotSpec(), 5);
        Assert.That(goal.Name, Is.EqualTo("Gather:iron_ingot"));
    }

    [Test]
    public void Description_IncludesTargetCountAndDisplayName()
    {
        var goal = new GenericGatherGoal(OakLogSpec(), 10);
        Assert.That(goal.Description, Does.Contain("10"));
        Assert.That(goal.Description, Does.Contain("Oak Log"));
    }

    [Test]
    public void Phases_AreCorrect()
    {
        var goal = new GenericGatherGoal(OakLogSpec(), 10);
        Assert.That(goal.Phases, Is.EqualTo(new[] { "FindSource", "Mine", "Collect" }));
    }

    [Test]
    public void Spec_ReturnsItemSpec()
    {
        var spec = OakLogSpec();
        var goal = new GenericGatherGoal(spec, 10);
        Assert.That(goal.Spec.ItemId, Is.EqualTo(spec.ItemId));
    }

    // ── IsComplete — non-smelting ─────────────────────────────────────────────

    [Test]
    public void IsComplete_EmptyInventory_ReturnsFalse()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState();
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_SingleSourceBlock_BelowTarget_ReturnsFalse()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState().With(b => b.AddInventoryItem("oak_log", 9));
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_SingleSourceBlock_ExactTarget_ReturnsTrue()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState().With(b => b.AddInventoryItem("oak_log", 10));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_SingleSourceBlock_AboveTarget_ReturnsTrue()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState().With(b => b.AddInventoryItem("oak_log", 15));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_MultipleSourceBlocks_SumsAll_ReturnsTrue()
    {
        // 4 oak_log + 3 birch_log + 4 spruce_log = 11 ≥ 10
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("oak_log",    4))
            .With(b => b.AddInventoryItem("birch_log",  3))
            .With(b => b.AddInventoryItem("spruce_log", 4));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_MultipleSourceBlocks_PartialSum_ReturnsFalse()
    {
        // 3 + 3 = 6 < 10
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("oak_log",   3))
            .With(b => b.AddInventoryItem("birch_log", 3));
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_NamespacedSourceBlock_StripsMinecraftPrefix()
    {
        // SourceBlocks has "minecraft:oak_log" — inventory has "oak_log" (no prefix)
        var goal  = new GenericGatherGoal(NamespacedSpec(), 5);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("oak_log",   3))
            .With(b => b.AddInventoryItem("birch_log", 3));
        Assert.That(goal.IsComplete(state), Is.True);  // 6 ≥ 5
    }

    [Test]
    public void IsComplete_DiamondSingleBlock_ExactTarget_ReturnsTrue()
    {
        var goal  = new GenericGatherGoal(DiamondSpec(), 3);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("diamond_ore",            1))
            .With(b => b.AddInventoryItem("deepslate_diamond_ore",  2));
        Assert.That(goal.IsComplete(state), Is.True);  // 1 + 2 = 3
    }

    // ── IsComplete — smelting ─────────────────────────────────────────────────

    [Test]
    public void IsComplete_RequiresSmelting_ChecksSmeltedProductOnly()
    {
        var goal  = new GenericGatherGoal(IronIngotSpec(), 5);
        // Has lots of raw iron ore but no smelted ingots
        var state = new WorldState()
            .With(b => b.AddInventoryItem("iron_ore", 20));
        Assert.That(goal.IsComplete(state), Is.False,
            "Raw ore must not count toward smelting goal completion.");
    }

    [Test]
    public void IsComplete_RequiresSmelting_SmeltedProductBelowTarget_ReturnsFalse()
    {
        var goal  = new GenericGatherGoal(IronIngotSpec(), 5);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("iron_ingot", 4));
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_RequiresSmelting_SmeltedProductExactTarget_ReturnsTrue()
    {
        var goal  = new GenericGatherGoal(IronIngotSpec(), 5);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("iron_ingot", 5));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_RequiresSmelting_OreAndIngot_OnlyIngotCounts()
    {
        var goal  = new GenericGatherGoal(IronIngotSpec(), 5);
        var state = new WorldState()
            .With(b => b.AddInventoryItem("iron_ore",   20))   // raw ore — ignored
            .With(b => b.AddInventoryItem("iron_ingot",  5));  // smelted product
        Assert.That(goal.IsComplete(state), Is.True);
    }

    // ── HasFailed ─────────────────────────────────────────────────────────────

    [Test]
    public void HasFailed_WhenFactNotSet_ReturnsFalse()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState();
        Assert.That(goal.HasFailed(state), Is.False);
    }

    [Test]
    public void HasFailed_WhenFactSetTrue_ReturnsTrue()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState()
            .With(b => b.SetFact("goal:Gather:oak_log:failed", true));
        Assert.That(goal.HasFailed(state), Is.True);
    }

    [Test]
    public void HasFailed_WhenFactSetFalse_ReturnsFalse()
    {
        var goal  = new GenericGatherGoal(OakLogSpec(), 10);
        var state = new WorldState()
            .With(b => b.SetFact("goal:Gather:oak_log:failed", false));
        Assert.That(goal.HasFailed(state), Is.False);
    }

    [Test]
    public void HasFailed_FactKeyIncludesItemId()
    {
        // Different item IDs must use different fact keys
        var oakGoal  = new GenericGatherGoal(OakLogSpec(),    10);
        var ironGoal = new GenericGatherGoal(IronIngotSpec(), 5);

        // Only oak_log is marked failed
        var state = new WorldState()
            .With(b => b.SetFact("goal:Gather:oak_log:failed", true));

        Assert.That(oakGoal.HasFailed(state),  Is.True);
        Assert.That(ironGoal.HasFailed(state), Is.False);
    }
}
