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
        var ev = new StatusEvent(new Position(1, 2, 3), 19, 17, inventory, Now);

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
        var ev = new StatusEvent(new Position(0, 0, 0), 20, 20, inventory, Now);

        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.ContainsKey("dirt"), Is.False,
            "Zero-quantity items are already excluded by WebSocketBridge; projector never sees them.");
    }

    // ── blockMined ────────────────────────────────────────────────────────────

    [Test]
    public void Apply_BlockMined_NamespacedId_IncrementsInventory()
    {
        var ev = new BlockMinedEvent("minecraft:oak_log", 1, new Position(0, 64, 0), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(1));
    }

    [Test]
    public void Apply_BlockMined_ShortId_IncrementsInventory()
    {
        var ev = new BlockMinedEvent("oak_log", 1, new Position(0, 64, 0), Now);
        var result = _projector.Apply(EmptyState, ev);

        Assert.That(result.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(1));
    }

    [Test]
    public void Apply_BlockMined_Twice_AccumulatesCount()
    {
        var ev     = new BlockMinedEvent("minecraft:oak_log", 1, new Position(0, 64, 0), Now);
        var after1 = _projector.Apply(EmptyState, ev);
        var after2 = _projector.Apply(after1, ev);

        Assert.That(after2.Inventory.GetValueOrDefault("oak_log"), Is.EqualTo(2));
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
}
