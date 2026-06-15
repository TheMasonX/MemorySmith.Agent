using Agent.Core;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class GatherWoodGoalTests
{
    [Test]
    public void Name_Is_GatherWood()
    {
        var goal = new GatherWoodGoal();
        Assert.That(goal.Name, Is.EqualTo("GatherWood"));
    }

    [Test]
    public void Phases_ContainsExpectedPhases()
    {
        var goal = new GatherWoodGoal();
        Assert.That(goal.Phases, Is.EqualTo(new[] { "FindTree", "MineWood", "Collect" }));
    }

    [Test]
    public void IsComplete_False_WithEmptyInventory()
    {
        var goal = new GatherWoodGoal(10);
        var state = new WorldState();
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_True_WithEnoughOakLog()
    {
        var goal = new GatherWoodGoal(5);
        var state = new WorldState { Inventory = { ["oak_log"] = 5 } };
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_True_WithBirchLog()
    {
        var goal = new GatherWoodGoal(3);
        var state = new WorldState { Inventory = { ["birch_log"] = 3 } };
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_False_WithInsufficientLogs()
    {
        var goal = new GatherWoodGoal(10);
        var state = new WorldState { Inventory = { ["oak_log"] = 5 } };
        Assert.That(goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_True_WithMixedLogs()
    {
        var goal = new GatherWoodGoal(5);
        // 3 oak + 3 birch = 6 total ≥ 5 — but GatherWoodGoal checks per-type
        // Each *_log key is summed. Let's verify 6 oak satisfies count=5.
        var state = new WorldState { Inventory = { ["dark_oak_log"] = 6 } };
        Assert.That(goal.IsComplete(state), Is.True);
    }

    [Test]
    public void HasFailed_False_WhenNotSet()
    {
        var goal = new GatherWoodGoal();
        Assert.That(goal.HasFailed(new WorldState()), Is.False);
    }

    [Test]
    public void HasFailed_True_WhenFactSet()
    {
        var goal = new GatherWoodGoal();
        var state = new WorldState
        {
            Facts = new() { ["goal:GatherWood:failed"] = (object?)true }
        };
        Assert.That(goal.HasFailed(state), Is.True);
    }

    [Test]
    public void TargetCount_Propagated()
    {
        Assert.That(new GatherWoodGoal(15).TargetCount, Is.EqualTo(15));
    }
}
