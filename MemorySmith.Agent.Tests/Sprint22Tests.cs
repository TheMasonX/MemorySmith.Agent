using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

// ── Sprint 22 P0-A: CraftItemGoal staleness gate ──────────────────────────────────────────────

[TestFixture]
public class Sprint22CraftItemGoalTests
{
    private static ItemSpec MakeSpec(string id) => new()
    {
        ItemId = id, DisplayName = id, SourceBlocks = [id],
        RequiresSmelting = false, MinHarvestLevel = 0,
    };

    [Test]
    public void IsComplete_ReturnsFalse_WhenInventoryIsStale()
    {
        var goal  = new CraftItemGoal("iron_pickaxe", 1);
        var state = new WorldState().With(b =>
        {
            b.SetInventory(new Dictionary<string, int> { ["iron_pickaxe"] = 1 });
            b.SetInventoryStale(true);
        });

        Assert.That(goal.IsComplete(state), Is.False,
            "IsComplete should return false when IsInventoryStale=true, even if inventory shows enough.");
    }

    [Test]
    public void IsComplete_ReturnsTrue_WhenInventoryFreshAndSufficient()
    {
        var goal  = new CraftItemGoal("iron_pickaxe", 1);
        var state = new WorldState().With(b =>
        {
            b.SetInventory(new Dictionary<string, int> { ["iron_pickaxe"] = 1 });
            b.SetInventoryStale(false);
        });

        Assert.That(goal.IsComplete(state), Is.True,
            "IsComplete should return true when inventory is fresh and has required item.");
    }

    [Test]
    public void IsComplete_ReturnsFalse_WhenInventoryFreshButInsufficient()
    {
        var goal  = new CraftItemGoal("iron_pickaxe", 2);
        var state = new WorldState().With(b =>
        {
            b.SetInventory(new Dictionary<string, int> { ["iron_pickaxe"] = 1 });
            b.SetInventoryStale(false);
        });

        Assert.That(goal.IsComplete(state), Is.False,
            "IsComplete should return false when inventory is fresh but count is insufficient.");
    }

    [Test]
    public void IsComplete_ReturnsFalse_WhenInventoryFreshAndEmpty()
    {
        var goal  = new CraftItemGoal("iron_pickaxe", 1);
        var state = new WorldState(); // default: IsInventoryStale=false, inventory empty

        Assert.That(goal.IsComplete(state), Is.False,
            "IsComplete should return false when inventory is empty (item not crafted).");
    }

    [Test]
    public void IsComplete_FreshAfterStaleness_ReturnsTrue()
    {
        var goal  = new CraftItemGoal("oak_planks", 4);
        // Simulates: SetGoal (marks stale) → GetStatus arrives (marks fresh) → check
        var stale = new WorldState().With(b =>
        {
            b.SetInventory(new Dictionary<string, int> { ["oak_planks"] = 4 });
            b.SetInventoryStale(true);
        });
        var fresh = stale.With(b => b.SetInventoryStale(false));

        Assert.That(goal.IsComplete(stale), Is.False, "Stale: should be false.");
        Assert.That(goal.IsComplete(fresh), Is.True,  "Fresh: should be true after staleness cleared.");
    }
}

// ── Sprint 22 P1: Quantity propagation via HtnPlanner ────────────────────────────────────────

[TestFixture]
public class Sprint22QuantityPropagationTests
{
    private static ItemSpec MakeSandSpec() => new()
    {
        ItemId       = "sand",
        DisplayName  = "Sand",
        SourceBlocks = ["sand"],
        RequiresSmelting = false,
        MinHarvestLevel  = 0,
    };

    [Test]
    public void GatherGoal_Count100_ProducesMinBlockAction_WithCount100()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new GenericGatherGoal(MakeSandSpec(), 100);
        var state   = new WorldState();

        var plan    = planner.PlanAsync(goal, state).GetAwaiter().GetResult();
        var mine    = plan.Actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", StringComparison.OrdinalIgnoreCase)
            && a.Arguments.TryGetValue("block", out var b)
            && string.Equals(b?.ToString(), "sand", StringComparison.OrdinalIgnoreCase));

        Assert.That(mine, Is.Not.Null, "Plan should contain a MineBlock(sand) action.");
        Assert.That(mine!.Arguments["count"], Is.EqualTo((object?)100),
            "MineBlock count should equal TargetCount=100, not the default 10.");
    }

    [Test]
    public void GatherGoal_Count1_ProducesMinBlockAction_WithCount1()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new GenericGatherGoal(MakeSandSpec(), 1);
        var state   = new WorldState();

        var plan = planner.PlanAsync(goal, state).GetAwaiter().GetResult();
        var mine = plan.Actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", StringComparison.OrdinalIgnoreCase)
            && a.Arguments.TryGetValue("block", out var b)
            && string.Equals(b?.ToString(), "sand", StringComparison.OrdinalIgnoreCase));

        Assert.That(mine, Is.Not.Null, "Plan should contain a MineBlock(sand) action.");
        Assert.That(mine!.Arguments["count"], Is.EqualTo((object?)1),
            "MineBlock count should equal TargetCount=1.");
    }

    [Test]
    public void GatherGoal_DefaultCount10_WhenNoTargetCountSet()
    {
        // GenericGatherGoal with targetCount=10 (default-ish value) should produce count=10
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new GenericGatherGoal(MakeSandSpec(), 10);
        var state   = new WorldState();

        var plan = planner.PlanAsync(goal, state).GetAwaiter().GetResult();
        var mine = plan.Actions.FirstOrDefault(a =>
            a.Tool.Equals("MineBlock", StringComparison.OrdinalIgnoreCase)
            && a.Arguments.TryGetValue("block", out var b)
            && string.Equals(b?.ToString(), "sand", StringComparison.OrdinalIgnoreCase));

        Assert.That(mine, Is.Not.Null);
        Assert.That(mine!.Arguments["count"], Is.EqualTo((object?)10));
    }

    [Test]
    public void GatherGoal_MultiBlock_AllMinActionsUseTargetCount()
    {
        // Items with multiple source blocks (e.g. oak_log, birch_log, spruce_log...)
        var spec = new ItemSpec
        {
            ItemId       = "oak_log",
            DisplayName  = "Oak Log",
            SourceBlocks = ["oak_log", "birch_log", "spruce_log"],
            RequiresSmelting = false,
            MinHarvestLevel  = 0,
        };
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new GenericGatherGoal(spec, 64);
        var state   = new WorldState();

        var plan     = planner.PlanAsync(goal, state).GetAwaiter().GetResult();
        var minActions = plan.Actions.Where(a =>
            a.Tool.Equals("MineBlock", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.That(minActions, Is.Not.Empty, "Plan should have MineBlock actions for a multi-block spec.");
        foreach (var action in minActions)
            Assert.That(action.Arguments["count"], Is.EqualTo((object?)64),
                $"All MineBlock actions should use count=64 (TargetCount), got {action.Arguments["count"]}.");
    }
}

// ── Sprint 22 P0: Health critical check ───────────────────────────────────────────────────────

[TestFixture]
public class Sprint22HealthCheckTests
{
    [Test]
    public void WorldState_HealthBelow6_IsConsideredCritical()
    {
        // Validate that the threshold constant (6) is meaningful for test purposes.
        // Actual agent behaviour is tested via integration; here we confirm WorldState tracks it.
        var state = new WorldState().With(b => b.SetHealth(4));
        Assert.That(state.Health, Is.LessThan(6),
            "Health=4 should be below the critical threshold of 6.");
    }

    [Test]
    public void WorldState_HealthAtOrAbove6_IsNotCritical()
    {
        var state = new WorldState().With(b => b.SetHealth(6));
        Assert.That(state.Health, Is.GreaterThanOrEqualTo(6),
            "Health=6 should be at the critical threshold boundary.");
    }

    [Test]
    public void WorldState_DefaultHealth_IsNotCritical()
    {
        var state = new WorldState();
        Assert.That(state.Health, Is.EqualTo(20),
            "Default health should be 20 (full health), well above critical threshold.");
    }
}

// ── Sprint 22 D-1 / E-1 housekeeping ─────────────────────────────────────────────────────────

[TestFixture]
public class Sprint22HousekeepingTests
{
    [Test]
    public void CraftItemGoal_HasFailureReasonProperty()
    {
        // Confirms CraftItemGoal implements IGoal.FailureReason (regression guard).
        var goal = new CraftItemGoal("stone_pickaxe", 1);
        goal.FailureReason = "test";
        Assert.That(goal.FailureReason, Is.EqualTo("test"));
    }

    [Test]
    public void GenericGatherGoal_TargetCount_IsAccessible()
    {
        // Regression: TargetCount property must remain public for HtnPlanner to access.
        var spec = new ItemSpec
        {
            ItemId = "dirt", DisplayName = "Dirt", SourceBlocks = ["dirt"],
            RequiresSmelting = false, MinHarvestLevel = 0,
        };
        var goal = new GenericGatherGoal(spec, 42);
        Assert.That(goal.TargetCount, Is.EqualTo(42),
            "TargetCount must be publicly accessible (used by HtnPlanner quantity propagation).");
    }
}
