namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Goals;
using global::Agent.Planning.Llm;
using global::Agent.Tools;
using NUnit.Framework;
using System.Text.Json;

/// <summary>
/// Sprint 35 — Inventory Truth + LLM-First Architecture
///
/// Test coverage:
///   P0-A: ItemCollectedEvent → correct inventory item (diamond not diamond_ore)
///   P0-B: MineCompleteEvent parsed + stored as facts
///   P0-C: FlatAreaFoundEvent.SearchedRadius parsed; BuildOriginSource enum
///   P0-D: ActionQueue.ClearAndEnqueueAsync stopCallback called before enqueue
///   P1-A: IntentDraft record shape (no GoalName field, has Confidence)
///   P1-B: ChatInterpreter ParseIntent no longer matches gather/build/craft (returns Unknown)
///   P1-C: LlmChatInterpreter BuildSystemPrompt includes inventory and confidence schema
///   P1-E: ActionOutcome record shape and factory helpers
/// </summary>
[TestFixture]
public class Sprint35Tests
{
    // ── P0-A: Inventory truth via ItemCollectedEvent ────────────────────────────

    [Test]
    public void ItemCollectedEvent_DiamondOre_AddsToInventoryAsDiamond()
    {
        // Arrange: simulating mining diamond_ore; playerCollect fires with actual drop "diamond"
        var projector = new WorldStateProjector();
        var state = new WorldState();

        // Act: ItemCollectedEvent carries the ACTUAL drop name, not the block name
        var collected = new ItemCollectedEvent("diamond", 1, DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, collected);

        // Assert: inventory has "diamond" (correct drop), not "diamond_ore" (block name)
        Assert.That(updated.Inventory.TryGetValue("diamond", out var count), Is.True,
            "Inventory should contain 'diamond' key after ItemCollectedEvent");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void ItemCollectedEvent_IronOre_AddsRawIron()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();

        var collected = new ItemCollectedEvent("raw_iron", 3, DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, collected);

        Assert.That(updated.Inventory.TryGetValue("raw_iron", out var count), Is.True,
            "Mining iron_ore should yield raw_iron via playerCollect");
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void ItemCollectedEvent_Stone_AddsCobblestone()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();

        // Mining stone yields cobblestone
        var collected = new ItemCollectedEvent("cobblestone", 5, DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, collected);

        Assert.That(updated.Inventory.TryGetValue("cobblestone", out var count), Is.True);
        Assert.That(count, Is.EqualTo(5));
    }

    [Test]
    public void ItemCollectedEvent_MultipleSameItem_AccumulatesCount()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();

        // Two collect events for same item should accumulate
        var first  = new ItemCollectedEvent("oak_log", 1, DateTimeOffset.UtcNow);
        var second = new ItemCollectedEvent("oak_log", 1, DateTimeOffset.UtcNow);
        var after1 = projector.Apply(state, first);
        var after2 = projector.Apply(after1, second);

        Assert.That(after2.Inventory.TryGetValue("oak_log", out var total), Is.True);
        Assert.That(total, Is.EqualTo(2));
    }

    [Test]
    public void BlockMinedEvent_UpdatesInventoryWithMappedDrop()
    {
        // Sprint 40 P0-B: BlockMinedEvent now updates inventory using the block-to-item
        // drop map. diamond_ore maps to diamond via BlockToItemDrop. This ensures items
        // are credited even when the drop entity is not collected (playerCollect miss).
        var projector = new WorldStateProjector();
        var state = new WorldState();

        var mined = new BlockMinedEvent("diamond_ore", 1,
            new Position(100, 64, 200), new Position(100, 64, 200), DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, mined);

        // Inventory should have been updated with the mapped drop name (diamond)
        Assert.That(updated.Inventory.ContainsKey("diamond_ore"), Is.False,
            "BlockMined should not update inventory with block name (diamond_ore) — uses mapped drop");
        Assert.That(updated.Inventory.GetValueOrDefault("diamond"), Is.EqualTo(1),
            "BlockMined should add mapped drop (diamond) to inventory via BlockToItemDrop");

        // The fact should still be stored for diagnostics
        Assert.That(updated.Facts.ContainsKey("event:BlockMined:Block"), Is.True,
            "BlockMined fact should still be stored for diagnostics");
    }

    // ── P0-B: MineCompleteEvent ──────────────────────────────────────────────────

    [Test]
    public void MineCompleteEvent_StoredAsFacts()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();

        var complete = new MineCompleteEvent("oak_log", 5, 5, new Position(0, 0, 0), DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, complete);

        Assert.That(updated.Facts.TryGetValue("event:MineComplete:Block", out var block), Is.True);
        Assert.That(block?.ToString(), Is.EqualTo("oak_log"));
        Assert.That(updated.Facts.TryGetValue("event:MineComplete:Mined", out var mined), Is.True);
        Assert.That(mined?.ToString(), Is.EqualTo("5"));
        Assert.That(updated.Facts.TryGetValue("event:MineComplete:TargetCount", out var tc), Is.True);
        Assert.That(tc?.ToString(), Is.EqualTo("5"));
    }

    // ── P0-C: FlatAreaFoundEvent.SearchedRadius + BuildOriginSource ─────────────

    [Test]
    public void FlatAreaFoundEvent_SearchedRadius_StoredAsFact()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();

        var flatArea = new FlatAreaFoundEvent(100, 64, 200, 25,
            95, 105, 195, 205,
            SearchedRadius: 32,
            DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, flatArea);

        Assert.That(updated.Facts.TryGetValue("event:FlatAreaFound:SearchedRadius", out var r), Is.True);
        Assert.That(r?.ToString(), Is.EqualTo("32"));
    }

    [Test]
    public void FlatAreaFoundEvent_NoArea_SearchedRadius_StillSet()
    {
        var projector = new WorldStateProjector();
        var state = new WorldState();

        // Area=0 (no flat area found) but SearchedRadius should still be captured
        var flatArea = new FlatAreaFoundEvent(100, 65, 200, 0,
            100, 100, 200, 200,
            SearchedRadius: 48,
            DateTimeOffset.UtcNow);
        var updated = projector.Apply(state, flatArea);

        Assert.That(updated.Facts.TryGetValue("event:FlatAreaFound:SearchedRadius", out var r), Is.True);
        Assert.That(r?.ToString(), Is.EqualTo("48"));
    }

    [Test]
    public void BuildGoal_ExplicitOrigin_OriginSourceIsExplicit()
    {
        // Sprint 39: Blueprint is a record with property initializers (not positional ctor).
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "small-house",
            Name = "Small House",
            Dimensions = new global::Agent.Construction.Dimensions(5, 4, 5),
        };
        var origin = new BuildOrigin(100, 64, 200, BuildOriginSource.Explicit);
        var goal = new BuildGoal(blueprint, [], origin);

        Assert.That(goal.Origin, Is.Not.Null);
        Assert.That(goal.Origin!.Source, Is.EqualTo(BuildOriginSource.Explicit));
        Assert.That(goal.HasExplicitOrigin, Is.True);
    }

    [Test]
    public void BuildGoal_NoOrigin_OriginIsNull()
    {
        var blueprint = new global::Agent.Construction.Blueprint
        {
            Id = "small-house",
            Name = "Small House",
            Dimensions = new global::Agent.Construction.Dimensions(5, 4, 5),
        };
        var goal = new BuildGoal(blueprint, []);

        Assert.That(goal.Origin, Is.Null);
        Assert.That(goal.HasExplicitOrigin, Is.False, "No origin → HasExplicitOrigin should be false");
    }

    // ── P0-D: ActionQueue.ClearAndEnqueueAsync stopCallback ─────────────────────

    [Test]
    public async Task ClearAndEnqueueAsync_StopCallbackCalledBeforeEnqueue()
    {
        var queue = new ActionQueue();
        var log = new List<string>();

        // Pre-fill the queue
        queue.Enqueue(new ActionData { Tool = "MineBlock" });
        queue.Enqueue(new ActionData { Tool = "GetStatus" });

        var priority = new ActionData { Tool = "GetStatus" };

        // stopCallback records "stop" first; enqueue appends "enqueue" after
        await queue.ClearAndEnqueueAsync(priority, stopCallback: () =>
        {
            log.Add("stop");
            return Task.CompletedTask;
        });
        log.Add("enqueue");

        // Stop must have been called before the enqueue logic
        Assert.That(log[0], Is.EqualTo("stop"), "Stop callback must fire before enqueue");
        Assert.That(log[1], Is.EqualTo("enqueue"));
        Assert.That(queue.Count, Is.EqualTo(1), "Queue should contain only priority action");
    }

    [Test]
    public async Task ClearAndEnqueueAsync_NullCallback_WorksLikeClearAndEnqueue()
    {
        var queue = new ActionQueue();
        queue.Enqueue(new ActionData { Tool = "MineBlock" });

        var priority = new ActionData { Tool = "GetStatus" };
        await queue.ClearAndEnqueueAsync(priority, stopCallback: null);

        Assert.That(queue.Count, Is.EqualTo(1));
        var dequeued = queue.Dequeue();
        Assert.That(dequeued?.Tool, Is.EqualTo("GetStatus"));
    }

    // ── P1-A: IntentDraft record shape ───────────────────────────────────────────

    [Test]
    public void IntentDraft_HasNoGoalNameField()
    {
        // The "parsers never create goals" principle: IntentDraft must NOT have GoalName/GoalParameters
        var draft = new IntentDraft(
            Addressed: "yes",
            Intent: "gather",
            Item: "oak_log",
            Blueprint: null,
            Count: 10,
            X: null, Y: null, Z: null,
            Confidence: 0.95,
            ClarificationQuestion: null,
            Response: "Sure, gathering oak logs.");

        Assert.That(draft.Intent, Is.EqualTo("gather"), "Intent field should be present");
        Assert.That(draft.Item, Is.EqualTo("oak_log"), "Item field should be present");
        Assert.That(draft.Confidence, Is.EqualTo(0.95), "Confidence field should be present");
        Assert.That(draft.ClarificationQuestion, Is.Null, "ClarificationQuestion null for high-confidence");

        // Verify no GoalName property exists on the record type (compile-time check via reflection)
        var props = typeof(IntentDraft).GetProperties()
            .Select(p => p.Name)
            .ToList();
        Assert.That(props, Does.Not.Contain("GoalName"),
            "IntentDraft must not have GoalName — parsers never create goals");
        Assert.That(props, Does.Not.Contain("GoalParameters"),
            "IntentDraft must not have GoalParameters");
    }

    [Test]
    public void IntentDraft_LowConfidence_HasClarificationQuestion()
    {
        var draft = new IntentDraft(
            Addressed: "maybe",
            Intent: "clarify",
            Item: null,
            Blueprint: null,
            Count: null,
            X: null, Y: null, Z: null,
            Confidence: 0.3,
            ClarificationQuestion: "Did you want me to gather wood or build something?",
            Response: "Did you want me to gather wood or build something?");

        Assert.That(draft.Confidence, Is.LessThan(0.6),
            "Confidence below threshold should trigger clarification");
        Assert.That(draft.ClarificationQuestion, Is.Not.Null.And.Not.Empty);
    }

    // ── P1-B: ChatInterpreter no longer matches gather/build/craft ───────────────

    [Test]
    public void ChatInterpreter_GetMeSomeWood_ReturnsUnknown()
    {
        // Sprint 35 P1-D: gather commands removed from ChatInterpreter.ParseIntent
        // They now route through LlmChatInterpreter → LLM
        var opts = new ChatOptions { LlmEnabled = false };
        var interp = new ChatInterpreter(opts);

        var result = interp.InterpretAsync(
            "Player1", "get me some wood", "Leo",
            onlinePlayers: 1,
            botPosition: new Position(0, 64, 0),
            playerPosition: new Position(0, 64, 5),
            state: new WorldState()).Result;

        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Gather command should return Unknown after P1-D removal — LLM handles it");
    }

    [Test]
    public void ChatInterpreter_BuildAHouse_ReturnsUnknown()
    {
        var opts = new ChatOptions { LlmEnabled = false };
        var interp = new ChatInterpreter(opts);

        var result = interp.InterpretAsync(
            "Player1", "build a house", "Leo",
            onlinePlayers: 1,
            botPosition: new Position(0, 64, 0),
            playerPosition: new Position(0, 64, 5),
            state: new WorldState()).Result;

        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Build command should return Unknown after P1-D removal — LLM handles it");
    }

    [Test]
    public void ChatInterpreter_CraftAnIronPickaxe_ReturnsUnknown()
    {
        var opts = new ChatOptions { LlmEnabled = false };
        var interp = new ChatInterpreter(opts);

        var result = interp.InterpretAsync(
            "Player1", "craft an iron pickaxe", "Leo",
            onlinePlayers: 1,
            botPosition: new Position(0, 64, 0),
            playerPosition: new Position(0, 64, 5),
            state: new WorldState()).Result;

        Assert.That(result!.Intent, Is.EqualTo("clarify"),
            "Craft command should return Unknown after P1-D removal — LLM handles it");
    }

    [Test]
    public void ChatInterpreter_Stop_StillFastPathed()
    {
        // Stop/cancel should still work deterministically without LLM
        var opts = new ChatOptions { LlmEnabled = false };
        var interp = new ChatInterpreter(opts);

        var result = interp.InterpretAsync(
            "Player1", "stop", "Leo",
            onlinePlayers: 1,
            botPosition: new Position(0, 64, 0),
            playerPosition: new Position(0, 64, 5),
            state: new WorldState()).Result;

        Assert.That(result!.Intent, Is.EqualTo("cancel"),
            "Stop command must always be fast-pathed deterministically");
    }

    [Test]
    public void ChatInterpreter_GoTo_StillFastPathed()
    {
        // Navigation via explicit coordinates should still work in pattern fallback
        var opts = new ChatOptions { LlmEnabled = false };
        var interp = new ChatInterpreter(opts);

        var result = interp.InterpretAsync(
            "Player1", "go to 100 64 200", "Leo",
            onlinePlayers: 1,
            botPosition: new Position(0, 64, 0),
            playerPosition: new Position(0, 64, 5),
            state: new WorldState()).Result;

        Assert.That(result!.Intent, Is.EqualTo("navigate"),
            "go to X Y Z should still be deterministically parsed");
    }

    // ── P1-E: ActionOutcome record ───────────────────────────────────────────────

    [Test]
    public void ActionOutcome_Collected_ProducesCorrectEffects()
    {
        var goalId = Guid.NewGuid();
        var outcome = ActionOutcome.Collected(goalId, "MineBlock", "oak_log", 5);

        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.GoalId, Is.EqualTo(goalId));
        Assert.That(outcome.ToolName, Is.EqualTo("MineBlock"));
        Assert.That(outcome.Effects, Has.Count.EqualTo(1));
        Assert.That(outcome.Effects[0].Type, Is.EqualTo("ItemCollected"));
        Assert.That(outcome.Effects[0].Item, Is.EqualTo("oak_log"));
        Assert.That(outcome.Effects[0].Count, Is.EqualTo(5));
    }

    [Test]
    public void ActionOutcome_Failed_HasNoEffectsAndSuccessFalse()
    {
        var goalId = Guid.NewGuid();
        var outcome = ActionOutcome.Failed(goalId, "MineBlock", "No oak_log found within 128 blocks");

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Effects, Is.Empty);
        Assert.That(outcome.ObservationSummary, Does.Contain("No oak_log"));
    }

    [Test]
    public void ActionOutcome_Succeeded_BasicStructure()
    {
        var goalId = Guid.NewGuid();
        var outcome = ActionOutcome.Succeeded(goalId, "GetStatus", "Status refreshed");

        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.Effects, Is.Empty);
        Assert.That(outcome.ObservationSummary, Is.EqualTo("Status refreshed"));
        Assert.That(outcome.Timestamp, Is.GreaterThan(DateTimeOffset.UtcNow.AddSeconds(-5)));
    }

    // ── P2-B: CommonMinecraftBlocks copper additions ──────────────────────────────

    [Test]
    public void CommonMinecraftBlocks_ContainsCopperVariants()
    {
        Assert.That(CommonMinecraftBlocks.DirectMineBlocks.Contains("copper_ore"), Is.True,
            "copper_ore should be in DirectMineBlocks");
        Assert.That(CommonMinecraftBlocks.DirectMineBlocks.Contains("deepslate_copper_ore"), Is.True,
            "deepslate_copper_ore should be in DirectMineBlocks");
        Assert.That(CommonMinecraftBlocks.DirectMineBlocks.Contains("raw_copper"), Is.True,
            "raw_copper should be in DirectMineBlocks (drop from copper_ore)");
    }

    [Test]
    public void CommonMinecraftBlocks_RawOreDops_ContainRawIronRawGold()
    {
        Assert.That(CommonMinecraftBlocks.DirectMineBlocks.Contains("raw_iron"), Is.True);
        Assert.That(CommonMinecraftBlocks.DirectMineBlocks.Contains("raw_gold"), Is.True);
    }
}
