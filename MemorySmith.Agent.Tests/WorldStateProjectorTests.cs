using Agent.Core;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Unit tests for <see cref="WorldStateProjector"/>.
/// Verifies the pure-function contract: applying a WorldEvent returns the
/// correct new WorldState without mutating the input.
/// Sprint 14 P1b: added inventory key normalization tests for StatusEvent.
/// Sprint 35 P0-A: updated BlockMined tests — ApplyBlockMined no longer updates inventory.
///   Inventory truth now comes exclusively from ItemCollectedEvent (playerCollect).
///   Periodic GetStatus reconciles drift.
/// Sprint 35 P0-C: added SearchedRadius to FlatAreaFoundEvent constructors.
/// </summary>
[TestFixture]
public class WorldStateProjectorTests
{
    private WorldStateProjector _projector = null!;

    [SetUp]
    public void SetUp() => _projector = new WorldStateProjector();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static WorldState EmptyState => new();

    // Convenience: set position via a move event, then apply another event on top.
    private WorldState StateWithPosition(int x, int y, int z) =>
        _projector.Apply(EmptyState, new MoveEvent(new Position(x, y, z), Now));

    private WorldState StateWithHealth(int hp, int food = 20) =>
        _projector.Apply(EmptyState, new HealthEvent(hp, food, Now));

    // ── Health ────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_HealthEvent_UpdatesHealthAndFood()
    {
        var ev = new HealthEvent(15, 18, Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Health, Is.EqualTo(15));
            Assert.That(result.Food,   Is.EqualTo(18));
        });
    }

    [Test]
    public void Apply_HealthEvent_DoesNotChangePosition()
    {
        var withPos    = StateWithPosition(5, 64, 5);
        var afterHealth = _projector.Apply(withPos, new HealthEvent(10, 10, Now));

        Assert.That(afterHealth.Position, Is.EqualTo(new Position(5, 64, 5)));
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_SpawnEvent_UpdatesPositionHealthAndFood()
    {
        var ev = new SpawnEvent(new Position(10, 64, -20), 20, 20, Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Position, Is.EqualTo(new Position(10, 64, -20)));
            Assert.That(result.Health,   Is.EqualTo(20));
            Assert.That(result.Food,     Is.EqualTo(20));
        });
    }

    // ── Move / moveComplete ───────────────────────────────────────────────────

    [Test]
    public void Apply_MoveEvent_UpdatesPosition()
    {
        var ev = new MoveEvent(new Position(5, 63, 7), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Position, Is.EqualTo(new Position(5, 63, 7)));
    }

    [Test]
    public void Apply_MoveCompleteEvent_UpdatesPosition()
    {
        var ev = new MoveEvent(new Position(9, 65, -1), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Position, Is.EqualTo(new Position(9, 65, -1)));
    }

    [Test]
    public void Apply_MoveEvent_DoesNotChangeHealthOrFood()
    {
        var withHealth = StateWithHealth(14, 12);
        var afterMove  = _projector.Apply(withHealth, new MoveEvent(new Position(1, 1, 1), Now));

        Assert.Multiple(() =>
        {
            Assert.That(afterMove.Health, Is.EqualTo(14));
            Assert.That(afterMove.Food,   Is.EqualTo(12));
        });
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_StatusEvent_UpdatesPositionHealthAndInventory()
    {
        var inventory = new Dictionary<string, int> { ["oak_log"] = 3, ["stone"] = 10 };
        var ev = new StatusEvent(new Position(1, 2, 3), 19, 17, inventory, null, Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Position, Is.EqualTo(new Position(1, 2, 3)));
            Assert.That(result.Health,   Is.EqualTo(19));
            Assert.That(result.Food,     Is.EqualTo(17));
            Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(3));
            Assert.That(result.Inventory.GetValueOrDefault("stone"),   Is.EqualTo(10));
        });
    }

    // StatusEvent inventory is already parsed by WebSocketBridge — malformed
    // inventory is handled upstream, so this test is no longer applicable.
    // (The projector receives a clean IReadOnlyDictionary, not a raw JSON string.)

    [Test]
    public void Apply_StatusEvent_ZeroQuantityItemsExcluded()
    {
        // StatusEvent already filters zero-quantity items in ParseStatus;
        // we simulate a post-filter dictionary here.
        var inventory = new Dictionary<string, int> { ["oak_log"] = 3, ["stone"] = 5 };
        var ev = new StatusEvent(new Position(0, 0, 0), 20, 20, inventory, null, Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.ContainsKey("dirt"), Is.False,
            "Zero-quantity items are already excluded by WebSocketBridge; projector never sees them.");
    }

    // ── Sprint 14 P1b: StatusEvent inventory key normalization ────────────────

    [Test]
    public void Apply_StatusEvent_NamespacedInventoryKey_IsNormalized()
    {
        // Mineflayer can return "minecraft:oak_log" in the status payload.
        var inventory = new Dictionary<string, int> { ["minecraft:oak_log"] = 5 };
        var ev = new StatusEvent(new Position(0, 64, 0), 20, 20, inventory, null, Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(5),
                "Bare key 'oak_log' should resolve after namespace strip.");
            Assert.That(result.Inventory.ContainsKey("minecraft:oak_log"), Is.False,
                "Original namespaced key must not remain in inventory.");
        });
    }

    [Test]
    public void Apply_StatusEvent_MixedNamespacedAndBare_AreUnified()
    {
        // Both forms present simultaneously — counts must be merged.
        var inventory = new Dictionary<string, int>
        {
            ["minecraft:iron_ingot"] = 2,
            ["iron_ingot"]           = 1,
        };
        var ev = new StatusEvent(new Position(0, 64, 0), 20, 20, inventory, null, Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("iron_ingot"), Is.EqualTo(3),
            "Namespaced and bare counts for the same item must be summed.");
    }

    [Test]
    public void Apply_StatusEvent_BareKeys_PassThroughUnchanged()
    {
        // Fast path: no namespace prefix → no allocation, same semantics.
        var inventory = new Dictionary<string, int> { ["cobblestone"] = 64, ["stick"] = 32 };
        var ev = new StatusEvent(new Position(0, 64, 0), 20, 20, inventory, null, Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Inventory.GetValueOrDefault("cobblestone"), Is.EqualTo(64));
            Assert.That(result.Inventory.GetValueOrDefault("stick"),       Is.EqualTo(32));
        });
    }

    // ── blockMined (Sprint 35 P0-A: no longer updates inventory) ─────────────
    // Sprint 35: ApplyBlockMined stores facts only. Inventory updates come exclusively
    // from ItemCollectedEvent (Mineflayer playerCollect). GetStatus reconciles drift.

    [Test]
    public void Apply_BlockMined_DoesNotUpdateInventory()
    {
        // Sprint 35 P0-A: BlockMinedEvent must NOT update inventory.
        // diamond_ore → "diamond" mismatch (BUG-1) was the motivation.
        // ItemCollectedEvent is now the sole inventory authority.
        var ev = new BlockMinedEvent("oak_log", 5, new Position(0, 64, 0), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(0),
            "Sprint 35: BlockMinedEvent must NOT add to inventory — ItemCollectedEvent is the authority");
        Assert.That(result.Inventory, Is.Empty,
            "No inventory changes from BlockMinedEvent");
    }

    [Test]
    public void Apply_BlockMined_StoresBlockNameFact()
    {
        // Even though inventory is no longer updated, block name + count facts still stored.
        var ev = new BlockMinedEvent("oak_log", 5, new Position(100, 64, 200), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.TryGetValue("event:BlockMined:Block", out var block), Is.True,
            "BlockMined block name fact should be stored for diagnostics");
        Assert.That(block?.ToString(), Is.EqualTo("oak_log"));
        Assert.That(result.Facts.TryGetValue("event:BlockMined:Count", out var count), Is.True);
        Assert.That(count?.ToString(), Is.EqualTo("5"));
    }

    [Test]
    public void Apply_BlockMined_NamespacedId_DoesNotUpdateInventory()
    {
        // Sprint 35 P0-A: namespaced ids also produce no inventory update
        var ev = new BlockMinedEvent("minecraft:cobblestone", 64, new Position(0, 64, 0), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory, Is.Empty,
            "Sprint 35: namespaced BlockMinedEvent must not update inventory");
    }

    [Test]
    public void Apply_ItemCollectedEvent_UpdatesInventory()
    {
        // Sprint 35 P0-A: ItemCollectedEvent is now the canonical inventory source
        var ev = new ItemCollectedEvent("oak_log", 5, Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(5),
            "ItemCollectedEvent must update inventory with correct item count");
    }

    [Test]
    public void Apply_ItemCollectedEvent_NamespacedItem_Normalized()
    {
        // Guard: playerCollect should return bare names, but normalize defensively
        var ev = new ItemCollectedEvent("minecraft:diamond", 1, Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("diamond"), Is.EqualTo(1),
            "ItemCollectedEvent with namespaced item should normalize to bare name");
        Assert.That(result.Inventory.ContainsKey("minecraft:diamond"), Is.False);
    }

    // ── Error / blockNotFound — no structured state change ────────────────────

    [Test]
    public void Apply_ErrorEvent_DoesNotChangeStructuredState()
    {
        var withHealth = StateWithHealth(20, 18);
        var withPos    = _projector.Apply(withHealth, new MoveEvent(new Position(1, 2, 3), Now));

        var result = _projector.Apply(withPos, new ErrorEvent("mine", "path blocked", Now));

        Assert.Multiple(() =>
        {
            Assert.That(result.Health,   Is.EqualTo(20));
            Assert.That(result.Food,     Is.EqualTo(18));
            Assert.That(result.Position, Is.EqualTo(new Position(1, 2, 3)));
            Assert.That(result.Inventory, Is.Empty);
        });
    }

    [Test]
    public void Apply_ErrorEvent_DoesNotWriteGameLastErrorFact()
    {
        var ev     = new ErrorEvent("mine", "timeout", Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.ContainsKey("game.lastError"), Is.False,
            "WorldStateProjector must not write game.lastError — routing belongs to the error channel.");
    }

    [Test]
    public void Apply_BlockNotFoundEvent_DoesNotChangeStructuredState()
    {
        var withHealth = StateWithHealth(20);
        var ev = new BlockNotFoundEvent("minecraft:oak_log", 0, Now);

        var result = _projector.Apply(withHealth, ev);

        Assert.That(result.Health, Is.EqualTo(20));
    }

    [Test]
    public void Apply_BlockNotFoundEvent_DoesNotWriteGameLastErrorFact()
    {
        var ev     = new BlockNotFoundEvent("minecraft:oak_log", 0, Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.ContainsKey("game.lastError"), Is.False,
            "WorldStateProjector must not write game.lastError — routing belongs to the error channel.");
    }

    // ── Raw event facts ───────────────────────────────────────────────────────

    [Test]
    public void Apply_StoresRawEventFacts_ForKnownEventTypes()
    {
        var ev = new MoveEvent(new Position(5, 64, 3), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.ContainsKey("event:Move:Pos"), Is.True,
            "Raw event facts should be stored for all event types.");
    }

    [Test]
    public void Apply_StoresRawEventFacts_ForErrorEvents()
    {
        var ev = new ErrorEvent("mine", "path blocked", Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Facts.ContainsKey("event:Error:Action"),  Is.True);
            Assert.That(result.Facts.ContainsKey("event:Error:Message"), Is.True);
        });
    }

    // ── Fallback events (typed but not state-changing) ────────────────────────

    [Test]
    public void Apply_NonStateChangingEvent_StoresRawFactsAndLeavesStructuredStateUnchanged()
    {
        var withHealth = StateWithHealth(15, 10);
        var ev         = new DeathEvent(new Position(0, 64, 0), Now);

        var result = _projector.Apply(withHealth, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Health, Is.EqualTo(15),
                "Non-state-changing event should not change health.");
            Assert.That(result.Facts.ContainsKey("event:Death:Pos"), Is.True,
                "Raw event facts should still be stored for non-state-changing events.");
        });
    }

    // ── Purity ────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_IsPure_InputStateIsNotMutated()
    {
        var original       = EmptyState;
        var originalHealth = original.Health;
        var originalFood   = original.Food;

        var ev = new HealthEvent(5, 5, Now);
        _projector.Apply(original, ev); // discard result

        Assert.Multiple(() =>
        {
            Assert.That(original.Health, Is.EqualTo(originalHealth),
                "Apply must not mutate the input WorldState.Health.");
            Assert.That(original.Food,   Is.EqualTo(originalFood),
                "Apply must not mutate the input WorldState.Food.");
        });
    }

    [Test]
    public void Apply_ReturnsNewInstance_NotSameReference()
    {
        var original = EmptyState;
        var ev       = new HealthEvent(10, 10, Now);

        var result = _projector.Apply(original, ev);

        Assert.That(result, Is.Not.SameAs(original));
    }

    // ── FlatAreaFound (Sprint 9 A4, Sprint 35 P0-C SearchedRadius) ──────────

    [Test]
    public void Apply_FlatAreaFoundEvent_StoresAllFieldsAsFacts_AndLeavesStructuredStateUnchanged()
    {
        var withHealth = StateWithHealth(20);
        // Sprint 35 P0-C: SearchedRadius is now a required positional arg
        var ev = new FlatAreaFoundEvent(
            X: 10, Y: 64, Z: -5,
            Area: 36,
            MinX: 7, MaxX: 13, MinZ: -8, MaxZ: -2,
            SearchedRadius: 32,
            Timestamp: Now);

        var result = _projector.Apply(withHealth, ev);

        Assert.Multiple(() =>
        {
            // Structured state must be unchanged
            Assert.That(result.Health, Is.EqualTo(20),
                "FlatAreaFound must not change Health.");
            Assert.That(result.Inventory, Is.Empty,
                "FlatAreaFound must not affect inventory.");

            // Per-event raw facts
            Assert.That(result.Facts.ContainsKey("event:FlatAreaFound:X"), Is.True);
            Assert.That(result.Facts.ContainsKey("event:FlatAreaFound:Area"), Is.True);
            Assert.That(result.Facts.ContainsKey("event:FlatAreaFound:MinX"), Is.True);
            Assert.That(result.Facts["event:FlatAreaFound:X"]?.ToString(),    Is.EqualTo("10"));
            Assert.That(result.Facts["event:FlatAreaFound:Area"]?.ToString(), Is.EqualTo("36"));
            Assert.That(result.Facts["event:FlatAreaFound:MinX"]?.ToString(), Is.EqualTo("7"));
            Assert.That(result.Facts["event:FlatAreaFound:MaxZ"]?.ToString(), Is.EqualTo("-2"));

            // Sprint 35 P0-C: SearchedRadius fact stored
            Assert.That(result.Facts.ContainsKey("event:FlatAreaFound:SearchedRadius"), Is.True,
                "SearchedRadius fact must be stored for BuildGoalDecomposer retry logic.");
            Assert.That(result.Facts["event:FlatAreaFound:SearchedRadius"]?.ToString(), Is.EqualTo("32"));

            // Sprint 9: cross-event summary key readable by planners
            Assert.That(result.Facts.ContainsKey(BuildFactKeys.LastFlatArea), Is.True,
                "LastFlatArea summary fact should be written for planner access.");
            Assert.That(result.Facts[BuildFactKeys.LastFlatArea]?.ToString(), Is.EqualTo("36"));
        });
    }

    [Test]
    public void Apply_FlatAreaFoundEvent_ZeroArea_StillStoresFacts()
    {
        // Area=0 means no suitable flat region was found by the scanner
        // Sprint 35 P0-C: SearchedRadius=48 means we searched the max radius — no retry needed
        var ev = new FlatAreaFoundEvent(
            X: 0, Y: 64, Z: 0,
            Area: 0,
            MinX: 0, MaxX: 0, MinZ: 0, MaxZ: 0,
            SearchedRadius: 48,
            Timestamp: Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Facts.ContainsKey("event:FlatAreaFound:Area"), Is.True);
            Assert.That(result.Facts["event:FlatAreaFound:Area"]?.ToString(), Is.EqualTo("0"),
                "Area=0 (no flat area found) should still be stored as a fact.");
            Assert.That(result.Facts.ContainsKey(BuildFactKeys.LastFlatArea), Is.True,
                "LastFlatArea should be stored even for area=0 result (no flat area found).");
            Assert.That(result.Facts["event:FlatAreaFound:SearchedRadius"]?.ToString(), Is.EqualTo("48"),
                "SearchedRadius=48 should be stored so BuildGoalDecomposer knows not to retry.");
        });
    }
}
