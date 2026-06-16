using Agent.Core;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Unit tests for <see cref="WorldStateProjector"/>.
/// Verifies the pure-function contract: applying a WorldEvent returns the
/// correct new WorldState without mutating the input.
/// </summary>
[TestFixture]
public class WorldStateProjectorTests
{
    private WorldStateProjector _projector = null!;

    [SetUp]
    public void SetUp() => _projector = new WorldStateProjector();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorldEvent MakeEvent(string type, Dictionary<string, object?> payload) =>
        new(type, payload, DateTimeOffset.UtcNow);

    private static WorldState EmptyState => new();

    // Convenience: set position via a move event, then apply another event on top.
    private WorldState StateWithPosition(int x, int y, int z) =>
        _projector.Apply(EmptyState, MakeEvent("move", new() { ["x"] = x, ["y"] = y, ["z"] = z }));

    private WorldState StateWithHealth(int hp, int food = 20) =>
        _projector.Apply(EmptyState, MakeEvent("health", new() { ["hp"] = hp, ["food"] = food }));

    // ── Health ────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_HealthEvent_UpdatesHealthAndFood()
    {
        var ev = MakeEvent("health", new() { ["hp"] = 15, ["food"] = 18 });

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
        var afterHealth = _projector.Apply(withPos, MakeEvent("health", new() { ["hp"] = 10, ["food"] = 10 }));

        Assert.That(afterHealth.Position, Is.EqualTo(new Position(5, 64, 5)));
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_SpawnEvent_UpdatesPositionHealthAndFood()
    {
        var ev = MakeEvent("spawn", new()
        {
            ["x"] = 10, ["y"] = 64, ["z"] = -20,
            ["hp"] = 20, ["food"] = 20
        });

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
        var ev = MakeEvent("move", new() { ["x"] = 5, ["y"] = 63, ["z"] = 7 });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Position, Is.EqualTo(new Position(5, 63, 7)));
    }

    [Test]
    public void Apply_MoveCompleteEvent_UpdatesPosition()
    {
        var ev = MakeEvent("moveComplete", new() { ["x"] = 9, ["y"] = 65, ["z"] = -1 });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Position, Is.EqualTo(new Position(9, 65, -1)));
    }

    [Test]
    public void Apply_MoveEvent_DoesNotChangeHealthOrFood()
    {
        var withHealth = StateWithHealth(14, 12);
        var afterMove  = _projector.Apply(withHealth, MakeEvent("move", new() { ["x"] = 1, ["y"] = 1, ["z"] = 1 }));

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
        var inventoryJson = "{\"oak_log\":3,\"stone\":10}";
        var ev = MakeEvent("status", new()
        {
            ["x"] = 1, ["y"] = 2, ["z"] = 3,
            ["hp"] = 19, ["food"] = 17,
            ["inventory"] = inventoryJson
        });

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

    [Test]
    public void Apply_StatusEvent_MalformedInventory_RetainsOtherUpdates()
    {
        var ev = MakeEvent("status", new()
        {
            ["x"] = 7, ["y"] = 64, ["z"] = 3,
            ["hp"] = 10, ["food"] = 10,
            ["inventory"] = "not valid json {"
        });

        var result = _projector.Apply(EmptyState, ev);

        // Position and health should still update; inventory stays empty
        Assert.Multiple(() =>
        {
            Assert.That(result.Position, Is.EqualTo(new Position(7, 64, 3)));
            Assert.That(result.Health,   Is.EqualTo(10));
            Assert.That(result.Inventory, Is.Empty,
                "Malformed inventory JSON should leave inventory unchanged.");
        });
    }

    [Test]
    public void Apply_StatusEvent_ZeroQuantityItemsExcluded()
    {
        var inventoryJson = "{\"oak_log\":3,\"dirt\":0,\"stone\":5}";
        var ev = MakeEvent("status", new()
        {
            ["x"] = 0, ["y"] = 0, ["z"] = 0, ["hp"] = 20, ["food"] = 20,
            ["inventory"] = inventoryJson
        });

        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.ContainsKey("dirt"), Is.False,
            "Items with quantity 0 should not appear in inventory.");
    }

    // ── blockMined ────────────────────────────────────────────────────────────

    [Test]
    public void Apply_BlockMined_NamespacedId_IncrementsInventory()
    {
        var ev = MakeEvent("blockMined", new() { ["block"] = "minecraft:oak_log" });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(1));
    }

    [Test]
    public void Apply_BlockMined_ShortId_IncrementsInventory()
    {
        var ev = MakeEvent("blockMined", new() { ["block"] = "oak_log" });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(1));
    }

    [Test]
    public void Apply_BlockMined_Twice_AccumulatesCount()
    {
        var ev     = MakeEvent("blockMined", new() { ["block"] = "minecraft:oak_log" });
        var after1 = _projector.Apply(EmptyState, ev);
        var after2 = _projector.Apply(after1, ev);

        Assert.That(after2.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(2));
    }

    // ── Error / blockNotFound — no structured state change ────────────────────

    [Test]
    public void Apply_ErrorEvent_DoesNotChangeStructuredState()
    {
        var withHealth = StateWithHealth(20, 18);
        var withPos    = _projector.Apply(withHealth, MakeEvent("move", new() { ["x"] = 1, ["y"] = 2, ["z"] = 3 }));

        var result = _projector.Apply(withPos, MakeEvent("error", new()
        {
            ["action"] = "mine", ["message"] = "path blocked"
        }));

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
        var ev     = MakeEvent("error", new() { ["action"] = "mine", ["message"] = "timeout" });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.ContainsKey("game.lastError"), Is.False,
            "WorldStateProjector must not write game.lastError — routing belongs to the error channel.");
    }

    [Test]
    public void Apply_BlockNotFoundEvent_DoesNotChangeStructuredState()
    {
        var withHealth = StateWithHealth(20);
        var ev = MakeEvent("blockNotFound", new() { ["block"] = "minecraft:oak_log", ["mined"] = 0 });

        var result = _projector.Apply(withHealth, ev);

        Assert.That(result.Health, Is.EqualTo(20));
    }

    [Test]
    public void Apply_BlockNotFoundEvent_DoesNotWriteGameLastErrorFact()
    {
        var ev     = MakeEvent("blockNotFound", new() { ["block"] = "minecraft:oak_log", ["mined"] = 0 });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.ContainsKey("game.lastError"), Is.False,
            "WorldStateProjector must not write game.lastError — routing belongs to the error channel.");
    }

    // ── Raw event facts ───────────────────────────────────────────────────────

    [Test]
    public void Apply_StoresRawEventFacts_ForKnownEventTypes()
    {
        var ev = MakeEvent("move", new() { ["x"] = 5, ["y"] = 64, ["z"] = 3 });
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Facts.ContainsKey("event:move:x"), Is.True,
            "Raw event facts should be stored for all event types.");
    }

    [Test]
    public void Apply_StoresRawEventFacts_ForErrorEvents()
    {
        var ev = MakeEvent("error", new() { ["action"] = "mine", ["message"] = "path blocked" });
        var result = _projector.Apply(EmptyState, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Facts.ContainsKey("event:error:action"),  Is.True);
            Assert.That(result.Facts.ContainsKey("event:error:message"), Is.True);
        });
    }

    // ── Unknown event ─────────────────────────────────────────────────────────

    [Test]
    public void Apply_UnknownEventType_StoresRawFactsAndLeavesStructuredStateUnchanged()
    {
        var withHealth = StateWithHealth(15, 10);
        var ev         = MakeEvent("customEvent", new() { ["someKey"] = "someValue" });

        var result = _projector.Apply(withHealth, ev);

        Assert.Multiple(() =>
        {
            Assert.That(result.Health, Is.EqualTo(15),
                "Unknown event should not change health.");
            Assert.That(result.Facts.ContainsKey("event:customEvent:someKey"), Is.True);
        });
    }

    // ── Purity ────────────────────────────────────────────────────────────────

    [Test]
    public void Apply_IsPure_InputStateIsNotMutated()
    {
        var original       = EmptyState;
        var originalHealth = original.Health;
        var originalFood   = original.Food;

        var ev = MakeEvent("health", new() { ["hp"] = 5, ["food"] = 5 });
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
        var ev       = MakeEvent("health", new() { ["hp"] = 10, ["food"] = 10 });

        var result = _projector.Apply(original, ev);

        Assert.That(result, Is.Not.SameAs(original));
    }
}
