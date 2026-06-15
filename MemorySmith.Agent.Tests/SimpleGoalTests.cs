using Agent.Core;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class SimpleGoalTests
{
    [Test]
    public void Name_And_Description_ArePropagated()
    {
        var goal = new SimpleGoal("MyGoal", "Find treasure", ["Phase1"], _ => false);
        Assert.That(goal.Name, Is.EqualTo("MyGoal"));
        Assert.That(goal.Description, Is.EqualTo("Find treasure"));
    }

    [Test]
    public void Phases_ArePropagated()
    {
        var goal = new SimpleGoal("G", "", ["A", "B", "C"], _ => false);
        Assert.That(goal.Phases, Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void IsComplete_UsesProvidedPredicate()
    {
        var state = new WorldState { Health = 20 };
        var goal = new SimpleGoal("G", "", [], s => s.Health == 20);

        Assert.That(goal.IsComplete(state), Is.True);
        Assert.That(goal.IsComplete(state with { Health = 10 }), Is.False);
    }

    [Test]
    public void HasFailed_DefaultsToFalse()
    {
        var goal = new SimpleGoal("G", "", [], _ => false);
        Assert.That(goal.HasFailed(new WorldState()), Is.False);
    }

    [Test]
    public void HasFailed_UsesProvidedPredicate()
    {
        var goal = new SimpleGoal("G", "", [], _ => false,
            hasFailed: s => s.Health <= 0);

        Assert.That(goal.HasFailed(new WorldState { Health = 0 }), Is.True);
        Assert.That(goal.HasFailed(new WorldState { Health = 1 }), Is.False);
    }
}
