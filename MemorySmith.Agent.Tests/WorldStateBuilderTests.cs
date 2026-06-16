using Agent.Core;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests for WorldState.Builder — the inventory mutation methods
/// (AddInventoryItem, SetInventory) that were added in Phase 3 to
/// support blockMined event tracking. These are pure-function tests
/// with no game or network dependency.
/// </summary>
[TestFixture]
public class WorldStateBuilderTests
{
    [Test]
    public void AddInventoryItem_NewItem_AddsWithCount()
    {
        var state = new WorldState();
        var updated = state.With(b => b.AddInventoryItem("oak_log", 3));
        Assert.That(updated.Inventory["oak_log"], Is.EqualTo(3));
    }

    [Test]
    public void AddInventoryItem_ExistingItem_Accumulates()
    {
        var state = new WorldState();
        var step1 = state.With(b => b.AddInventoryItem("oak_log", 5));
        var step2 = step1.With(b => b.AddInventoryItem("oak_log", 3));
        Assert.That(step2.Inventory["oak_log"], Is.EqualTo(8));
    }

    [Test]
    public void AddInventoryItem_NegativeDelta_Decrements()
    {
        var state = new WorldState();
        var step1 = state.With(b => b.AddInventoryItem("cobblestone", 10));
        var step2 = step1.With(b => b.AddInventoryItem("cobblestone", -4));
        Assert.That(step2.Inventory["cobblestone"], Is.EqualTo(6));
    }

    [Test]
    public void AddInventoryItem_ResultDropsToZero_RemovesKey()
    {
        var state = new WorldState();
        var step1 = state.With(b => b.AddInventoryItem("dirt", 3));
        var step2 = step1.With(b => b.AddInventoryItem("dirt", -3));
        Assert.That(step2.Inventory.ContainsKey("dirt"), Is.False);
    }

    [Test]
    public void AddInventoryItem_ResultBelowZero_RemovesKey()
    {
        var state = new WorldState();
        var step1 = state.With(b => b.AddInventoryItem("stone", 2));
        var step2 = step1.With(b => b.AddInventoryItem("stone", -10));
        Assert.That(step2.Inventory.ContainsKey("stone"), Is.False);
    }

    [Test]
    public void SetInventory_ReplacesEntireContents()
    {
        var state = new WorldState();
        var step1 = state.With(b => b.AddInventoryItem("old_item", 99));
        var snap  = new Dictionary<string, int> { ["oak_log"] = 5, ["stone"] = 12 };
        var step2 = step1.With(b => b.SetInventory(snap));

        Assert.That(step2.Inventory.ContainsKey("old_item"), Is.False);
        Assert.That(step2.Inventory["oak_log"], Is.EqualTo(5));
        Assert.That(step2.Inventory["stone"],   Is.EqualTo(12));
    }

    [Test]
    public void SetInventory_EmptySnapshot_ClearsAll()
    {
        var state = new WorldState();
        var step1 = state.With(b => b.AddInventoryItem("oak_log", 10));
        var step2 = step1.With(b => b.SetInventory(new Dictionary<string, int>()));
        Assert.That(step2.Inventory, Is.Empty);
    }

    [Test]
    public void SetFact_SetAndGet_RoundTrips()
    {
        var state   = new WorldState();
        var updated = state.With(b => b.SetFact("biome", "forest"));
        Assert.That(updated.Facts["biome"]?.ToString(), Is.EqualTo("forest"));
    }

    [Test]
    public void SetFact_OverwriteExisting_UpdatesValue()
    {
        var state  = new WorldState();
        var step1  = state.With(b => b.SetFact("biome", "forest"));
        var step2  = step1.With(b => b.SetFact("biome", "desert"));
        Assert.That(step2.Facts["biome"]?.ToString(), Is.EqualTo("desert"));
    }

    [Test]
    public void SetPosition_UpdatesPosition()
    {
        var state   = new WorldState();
        var updated = state.With(b => b.SetPosition(new Position(10, 64, -20)));
        Assert.Multiple(() =>
        {
            Assert.That(updated.Position.X, Is.EqualTo(10));
            Assert.That(updated.Position.Y, Is.EqualTo(64));
            Assert.That(updated.Position.Z, Is.EqualTo(-20));
        });
    }

    [Test]
    public void MultipleBuilderCalls_AreImmutable_OriginalUnchanged()
    {
        var original = new WorldState();
        var mutated  = original.With(b => b.AddInventoryItem("oak_log", 5).SetHealth(15));
        Assert.That(original.Inventory, Is.Empty);
        Assert.That(original.Health, Is.EqualTo(20));
        Assert.That(mutated.Inventory["oak_log"], Is.EqualTo(5));
        Assert.That(mutated.Health, Is.EqualTo(15));
    }
}
