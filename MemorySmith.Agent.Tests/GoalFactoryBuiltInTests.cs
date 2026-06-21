using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 13: tests for GoalFactory built-in fallback specs (dirt, snow, ores, etc.)
/// and CraftItem goal creation — both exercised without a live IItemRegistry.
/// </summary>
[TestFixture]
[Description("Sprint 13: GoalFactory built-in gather fallback and CraftItem support")]
public sealed class GoalFactoryBuiltInTests
{
    // ── Built-in gather fallback ──────────────────────────────────────────────

    [Test]
    public async Task CreateAsync_GatherDirt_ReturnsGoal_WithoutRegistry()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:dirt");
        Assert.That(goal, Is.Not.Null, "GoalFactory should return a goal for 'dirt' via built-in spec.");
    }

    [Test]
    public async Task CreateAsync_GatherSnow_ReturnsGoal()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:snow");
        Assert.That(goal, Is.Not.Null);
    }

    [Test]
    public async Task CreateAsync_GatherGravel_ReturnsGoal()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:gravel");
        Assert.That(goal, Is.Not.Null);
    }

    [Test]
    public async Task CreateAsync_GatherIronOre_ReturnsGoal()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:iron_ore");
        Assert.That(goal, Is.Not.Null);
    }

    [Test]
    public async Task CreateAsync_GatherOakLog_ReturnsGoal_WithBuiltInSpec()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:oak_log");
        Assert.That(goal, Is.Not.Null,
            "oak_log is in the built-in spec list and should work without a registry.");
    }

    [Test]
    public async Task CreateAsync_GatherDirt_Count_IsRespected()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:dirt",
            new Dictionary<string, object?> { ["count"] = 5 });

        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.IsComplete(new WorldState().With(b => b.AddInventoryItem("dirt", 5))), Is.True,
            "Goal with count=5 should complete when inventory has 5 dirt.");
        Assert.That(goal.IsComplete(new WorldState().With(b => b.AddInventoryItem("dirt", 4))), Is.False,
            "Goal with count=5 should not complete when inventory has only 4 dirt.");
    }

    [Test]
    public async Task CreateAsync_GatherDirt_GoalNameContainsItemId()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("GatherItem:dirt");
        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.Name, Does.Contain("dirt").IgnoreCase);
    }

    // ── CraftItem goal creation ───────────────────────────────────────────────

    [Test]
    public async Task CreateAsync_CraftIronPickaxe_ReturnsGoal()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("CraftItem:iron_pickaxe",
            new Dictionary<string, object?> { ["count"] = 1 });

        Assert.That(goal, Is.Not.Null, "GoalFactory must create CraftItemGoal for 'CraftItem:iron_pickaxe'.");
        Assert.That(goal!.Name, Is.EqualTo("CraftItem:iron_pickaxe"));
    }

    [Test]
    public async Task CreateAsync_CraftStick_DefaultCountIsOne()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("CraftItem:stick") as CraftItemGoal;

        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_CraftItem_CountParameter_IsRespected()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        var goal    = await factory.CreateAsync("CraftItem:stick",
            new Dictionary<string, object?> { ["count"] = 8 }) as CraftItemGoal;

        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.Count, Is.EqualTo(8));
    }

    [Test]
    public void RegisteredGoals_IncludesCraftItemPrefix()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        Assert.That(factory.RegisteredGoals, Has.Some.EqualTo("CraftItem:{itemId}"),
            "Registered goals should advertise the CraftItem prefix.");
    }

    [Test]
    public void RegisteredGoals_IncludesGatherItemPrefix()
    {
        var factory = new GoalFactory(itemRegistry: null, blueprintRepository: null);
        Assert.That(factory.RegisteredGoals, Has.Some.EqualTo("GatherItem:{itemId}"));
    }
}
