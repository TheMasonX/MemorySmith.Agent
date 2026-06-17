using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 13: tests for CraftItemGoal creation, completion logic, and HtnPlanner routing.
/// Sprint 14 P0: added iron pickaxe pre-gather tests.
/// </summary>
[TestFixture]
[Description("Sprint 13: CraftItemGoal and HtnPlanner crafting decomposition")]
public sealed class CraftItemGoalTests
{
    // ── Goal construction ─────────────────────────────────────────────────────

    [Test]
    public void CraftItemGoal_Name_HasCorrectFormat()
    {
        var goal = new CraftItemGoal("iron_pickaxe", 1);
        Assert.That(goal.Name, Is.EqualTo("CraftItem:iron_pickaxe"));
    }

    [Test]
    public void CraftItemGoal_Description_ContainsItemAndCount()
    {
        var goal = new CraftItemGoal("iron_pickaxe", 2);
        Assert.That(goal.Description, Does.Contain("2").IgnoreCase);
        Assert.That(goal.Description, Does.Contain("iron").IgnoreCase);
    }

    [Test]
    public void CraftItemGoal_DefaultCount_IsOne()
    {
        var goal = new CraftItemGoal("stick");
        Assert.That(goal.Count, Is.EqualTo(1));
    }

    [Test]
    public void CraftItemGoal_Phases_ContainsCraft()
    {
        var goal = new CraftItemGoal("crafting_table");
        Assert.That(goal.Phases, Has.Member("Craft"));
    }

    // ── IsComplete ────────────────────────────────────────────────────────────

    [Test]
    public void CraftItemGoal_IsComplete_WhenInventorySufficient()
    {
        var goal  = new CraftItemGoal("iron_pickaxe", 1);
        var state = new WorldState().With(b => b.AddInventoryItem("iron_pickaxe", 1));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void CraftItemGoal_IsComplete_WhenInventoryExceedsCount()
    {
        var goal  = new CraftItemGoal("stick", 2);
        var state = new WorldState().With(b => b.AddInventoryItem("stick", 10));
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void CraftItemGoal_IsNotComplete_WhenInventoryInsufficient()
    {
        var goal  = new CraftItemGoal("iron_pickaxe", 2);
        var state = new WorldState().With(b => b.AddInventoryItem("iron_pickaxe", 1));
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void CraftItemGoal_IsNotComplete_WhenInventoryEmpty()
    {
        var goal = new CraftItemGoal("iron_pickaxe", 1);
        Assert.That(goal.IsComplete(new WorldState()), Is.False);
    }

    // ── HtnPlanner routing ────────────────────────────────────────────────────

    [Test]
    public async Task HtnPlanner_CraftItemGoal_EmitsCraftItemAction()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("stick", 4);
        var plan    = await planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.Actions.Any(a =>
            a.Tool.Equals("CraftItem", StringComparison.OrdinalIgnoreCase) &&
            a.Arguments.TryGetValue("item", out var item) &&
            item?.ToString() == "stick"), Is.True,
            "Plan must contain a CraftItem action for 'stick'.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_EndsWithGetStatus()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("oak_planks", 8);
        var plan    = await planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.Actions.Last().Tool, Is.EqualTo("GetStatus"),
            "CraftItem plan should end with GetStatus.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_TableRequiring_EmitsCraftingTableStep_WhenNoTableInInventory()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("wooden_pickaxe", 1);
        var state   = new WorldState(); // no crafting_table in inventory
        var plan    = await planner.PlanAsync(goal, state);

        Assert.That(plan.Actions.Any(a =>
            a.Tool == "CraftItem" &&
            a.Arguments.TryGetValue("item", out var item) &&
            item?.ToString() == "crafting_table"), Is.True,
            "Plan for wooden_pickaxe must include a crafting_table step when none in inventory.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_TableRequiring_SkipsCraftingTable_WhenAlreadyOwned()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("wooden_pickaxe", 1);
        var state   = new WorldState().With(b => b.AddInventoryItem("crafting_table", 1));
        var plan    = await planner.PlanAsync(goal, state);

        var tableActions = plan.Actions
            .Where(a => a.Tool == "CraftItem" &&
                        a.Arguments.TryGetValue("item", out var item) &&
                        item?.ToString() == "crafting_table")
            .ToList();

        Assert.That(tableActions, Is.Empty,
            "Should not craft a crafting_table when one is already in inventory.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_GoalNamePreserved()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("chest", 1);
        var plan    = await planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.GoalName, Is.EqualTo("CraftItem:chest"));
    }

    // ── Sprint 14 P0: iron pickaxe pre-gather ─────────────────────────────────

    [Test]
    public async Task HtnPlanner_CraftItemGoal_IronPickaxe_EmitsMineBlockAndSmelt_WhenNoIngots()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("iron_pickaxe", 1);
        var state   = new WorldState(); // empty inventory — no iron ingots, no ore

        var plan = await planner.PlanAsync(goal, state);
        var tools = plan.Actions.Select(a => a.Tool).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(tools, Has.Member("MineBlock"),
                "Plan must mine iron ore when no ingots available.");
            Assert.That(tools, Has.Member("SmeltItem"),
                "Plan must smelt iron ore to ingots before crafting.");
            // MineBlock must precede SmeltItem in the plan
            Assert.That(tools.IndexOf("MineBlock"), Is.LessThan(tools.IndexOf("SmeltItem")),
                "MineBlock must appear before SmeltItem.");
            // SmeltItem must precede the final CraftItem(iron_pickaxe)
            var craftPickaxeIdx = plan.Actions
                .Select((a, i) => (a, i))
                .Where(x => x.a.Tool == "CraftItem" &&
                            x.a.Arguments.TryGetValue("item", out var v) &&
                            v?.ToString() == "iron_pickaxe")
                .Select(x => x.i)
                .FirstOrDefault(-1);
            Assert.That(craftPickaxeIdx, Is.GreaterThan(tools.IndexOf("SmeltItem")),
                "SmeltItem must appear before the final CraftItem(iron_pickaxe).");
        });
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_IronPickaxe_SkipsMine_WhenOreAlreadyInInventory()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("iron_pickaxe", 1);
        // 3 iron_ore in inventory → enough to smelt to 3 ingots (pickaxe needs 3)
        var state   = new WorldState().With(b => b.AddInventoryItem("iron_ore", 3));

        var plan = await planner.PlanAsync(goal, state);

        var mineIronActions = plan.Actions
            .Where(a => a.Tool == "MineBlock" &&
                        a.Arguments.TryGetValue("block", out var b) &&
                        b?.ToString() == "iron_ore")
            .ToList();

        Assert.That(mineIronActions, Is.Empty,
            "Should not mine iron_ore when sufficient ore is already in inventory.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_IronPickaxe_SkipsPreGather_WhenIngotsSufficient()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("iron_pickaxe", 1);
        // 3 iron_ingots → no pre-gather needed
        var state   = new WorldState().With(b => b.AddInventoryItem("iron_ingot", 3));

        var plan = await planner.PlanAsync(goal, state);

        var preGatherActions = plan.Actions
            .Where(a => a.Tool is "MineBlock" or "SmeltItem")
            .ToList();

        Assert.That(preGatherActions, Is.Empty,
            "No MineBlock/SmeltItem should be emitted when iron ingots are already sufficient.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_StoneSword_EmitsMineBlock_WhenNoCobblestone()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("stone_sword", 1);
        var state   = new WorldState(); // empty inventory

        var plan = await planner.PlanAsync(goal, state);

        Assert.That(plan.Actions.Any(a =>
            a.Tool == "MineBlock" &&
            a.Arguments.TryGetValue("block", out var b) &&
            b?.ToString() == "stone"), Is.True,
            "Plan for stone_sword must mine cobblestone/stone when none in inventory.");
    }

    [Test]
    public async Task HtnPlanner_CraftItemGoal_StoneSword_SkipsMine_WhenCobblestonePresent()
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var goal    = new CraftItemGoal("stone_sword", 1);
        // stone_sword needs 2 cobblestone
        var state   = new WorldState().With(b => b.AddInventoryItem("cobblestone", 5));

        var plan = await planner.PlanAsync(goal, state);

        var mineStoneActions = plan.Actions
            .Where(a => a.Tool == "MineBlock" &&
                        a.Arguments.TryGetValue("block", out var bl) &&
                        bl?.ToString() == "stone")
            .ToList();

        Assert.That(mineStoneActions, Is.Empty,
            "Should not mine stone when sufficient cobblestone is already in inventory.");
    }
}
