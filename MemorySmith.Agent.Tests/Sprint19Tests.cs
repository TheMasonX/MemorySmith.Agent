using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;
using Agent.Planning.Llm;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Sprint 19: Tests for gather plan rework (conditional Wander), stone alias fix,
/// and yield-aware SourceBlocks in GoalFactory.
/// </summary>
[TestFixture]
public sealed class Sprint19Tests
{
    // ── Gather plan: conditional Wander ────────────────────────────────────────

    [Test]
    public void GatherPlan_NoBlockNotFound_OmitsWander()
    {
        var lib = new HtnTaskLibrary();
        var spec = new ItemSpec
        {
            ItemId = "dirt",
            DisplayName = "Dirt",
            SourceBlocks = ["dirt"],
            RequiresSmelting = false,
            MinHarvestLevel = 0,
        };
        var state = new WorldState(); // No BlockNotFound fact

        var actions = lib.DecomposeGatherItem(spec, ["10"], state);

        var toolNames = actions.Select(a => a.Tool).ToList();
        Assert.That(toolNames, Does.Not.Contain("Wander"),
            "Default gather plan should not include Wander — findBlock handles nearby blocks");
        Assert.That(toolNames, Does.Contain("MineBlock"));
        // Sprint 44 (TSK-0080): SearchMemory removed — results were never consumed downstream.
        // The adapter's bot.findBlock() handles spatial search locally.
        Assert.That(toolNames, Does.Not.Contain("SearchMemory"),
            "Sprint 44 TSK-0080: SearchMemory removed from gather plan — results were never consumed.");
        // Sprint 38 P0-A: GetStatus removed from GatherItemDecompose to fix the inventory reset
        // bug where GetStatus overwrote incremental collection. Verify it is NOT in the plan.
        Assert.That(toolNames, Does.Not.Contain("GetStatus"),
            "Sprint 38 P0-A: GetStatus must NOT appear in gather plan — inventory is event-driven now.");
    }

    [Test]
    public void GatherPlan_AfterBlockNotFound_IncludesWander()
    {
        var lib = new HtnTaskLibrary();
        var spec = new ItemSpec
        {
            ItemId = "dirt",
            DisplayName = "Dirt",
            SourceBlocks = ["dirt"],
            RequiresSmelting = false,
            MinHarvestLevel = 0,
        };
        // Simulate a previous BlockNotFound event for "dirt"
        var state = new WorldState().With(b =>
            b.SetFact("event:BlockNotFound:Block", "dirt", FactSource.Observed));  // Sprint 33 P1-3;

        var actions = lib.DecomposeGatherItem(spec, ["10"], state);

        var toolNames = actions.Select(a => a.Tool).ToList();
        Assert.That(toolNames, Does.Contain("Wander"),
            "After BlockNotFound, Wander should be included to explore a new area");
    }

    [Test]
    public void GatherPlan_BlockNotFoundForDifferentBlock_OmitsWander()
    {
        var lib = new HtnTaskLibrary();
        var spec = new ItemSpec
        {
            ItemId = "dirt",
            DisplayName = "Dirt",
            SourceBlocks = ["dirt"],
            RequiresSmelting = false,
            MinHarvestLevel = 0,
        };
        // BlockNotFound for a DIFFERENT block (stone, not dirt)
        var state = new WorldState().With(b =>
            b.SetFact("event:BlockNotFound:Block", "stone", FactSource.Observed));  // Sprint 33 P1-3;

        var actions = lib.DecomposeGatherItem(spec, ["10"], state);

        var toolNames = actions.Select(a => a.Tool).ToList();
        Assert.That(toolNames, Does.Not.Contain("Wander"),
            "BlockNotFound for a different block should not trigger Wander");
    }

    // ── Stone alias: ChatInterpreter ──────────────────────────────────────────

    [Test]
    public async Task StoneAlias_ResolvesToStone_NotCobblestone()
    {
        var opts = new ChatOptions
        {
            MaxMessageLength = 1024,
            ConversationWindowSeconds = 60,
        };
        var interp = new ChatInterpreter(opts);
        var state = new WorldState();
        var botPos = new Position(0, 64, 0);
        var playerPos = new Position(5, 64, 5);

        var result = await interp.InterpretAsync(
            "Player1", "get 10 stone", "Bot", 1, botPos, playerPos, state);

        // Sprint 39 P1-C: Gather is no longer fast-pathed by ChatInterpreter (Sprint 35 P1-D).
        // ChatInterpreter returns "clarify" so LlmChatInterpreter routes to the LLM.
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Gather routes to LLM via 'clarify' — stone alias resolution happens in LlmChatInterpreter.");
    }

    [Test]
    public async Task CobblestoneAlias_StillWorksSeparately()
    {
        var opts = new ChatOptions
        {
            MaxMessageLength = 1024,
            ConversationWindowSeconds = 60,
        };
        var interp = new ChatInterpreter(opts);
        var state = new WorldState();
        var botPos = new Position(0, 64, 0);
        var playerPos = new Position(5, 64, 5);

        var result = await interp.InterpretAsync(
            "Player1", "get cobblestone", "Bot", 1, botPos, playerPos, state);

        // Sprint 39 P1-C: same as stone — gather routes through LLM.
        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Cobblestone gather routes to LLM via 'clarify' from ChatInterpreter.");
    }

    // ── GoalFactory: yield-aware SourceBlocks ─────────────────────────────────

    [Test]
    public async Task GoalFactory_GatherStone_SourceBlocksIncludesCobblestone()
    {
        var factory = new GoalFactory();
        var goal = await factory.CreateAsync("GatherItem:stone", new Dictionary<string, object?> { ["count"] = 10 });

        Assert.That(goal, Is.Not.Null);
        Assert.That(goal, Is.InstanceOf<GenericGatherGoal>());
        var gatherGoal = (GenericGatherGoal)goal!;
        Assert.That(gatherGoal.Spec.SourceBlocks, Does.Contain("stone"),
            "Stone spec should include stone blocks");
        Assert.That(gatherGoal.Spec.SourceBlocks, Does.Contain("cobblestone"),
            "Stone spec should include cobblestone (yield from mining stone)");
    }

    [Test]
    public void GatherStone_MinedStoneYieldsCobblestone_GoalCompletes()
    {
        var spec = new ItemSpec
        {
            ItemId = "stone",
            DisplayName = "Stone",
            SourceBlocks = ["stone", "cobblestone"],
            RequiresSmelting = false,
            MinHarvestLevel = 0,
        };
        var goal = new GenericGatherGoal(spec, 10);

        // Bot mined stone blocks but received cobblestone in inventory
        var state = new WorldState().With(b => b.AddInventoryItem("cobblestone", 10));

        Assert.That(goal.IsComplete(state), Is.True,
            "Goal should complete when cobblestone count >= target (mining stone yields cobblestone)");
    }

    [Test]
    public void GatherStone_MixedInventory_CountsBoth()
    {
        var spec = new ItemSpec
        {
            ItemId = "stone",
            DisplayName = "Stone",
            SourceBlocks = ["stone", "cobblestone"],
            RequiresSmelting = false,
            MinHarvestLevel = 0,
        };
        var goal = new GenericGatherGoal(spec, 10);

        // Bot has some stone (from Silk Touch) and some cobblestone (from normal mining)
        var state = new WorldState().With(b =>
        {
            b.AddInventoryItem("stone", 4);
            b.AddInventoryItem("cobblestone", 6);
        });

        Assert.That(goal.IsComplete(state), Is.True,
            "Goal should count both stone and cobblestone toward completion");
    }
}
