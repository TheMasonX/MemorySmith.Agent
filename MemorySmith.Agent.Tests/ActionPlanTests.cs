using Agent.Core;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class ActionPlanTests
{
    [Test]
    public void Properties_Are_Propagated()
    {
        var actions = new ActionData[] { new() { Tool = "MoveTo" }, new() { Tool = "MineBlock" } };
        var plan = new ActionPlan("GatherWood", ["FindTree", "Mine"], actions);

        Assert.That(plan.GoalName, Is.EqualTo("GatherWood"));
        Assert.That(plan.Phases, Is.EqualTo(new[] { "FindTree", "Mine" }));
        Assert.That(plan.Actions, Has.Count.EqualTo(2));
    }

    [Test]
    public void IsEmpty_True_WhenNoActions()
    {
        var plan = new ActionPlan("Test", [], []);
        Assert.That(plan.IsEmpty, Is.True);
    }

    [Test]
    public void IsEmpty_False_WhenActionsExist()
    {
        var plan = new ActionPlan("Test", [], [new ActionData { Tool = "GetStatus" }]);
        Assert.That(plan.IsEmpty, Is.False);
    }

    [Test]
    public void ToString_ContainsGoalAndActionCount()
    {
        var plan = new ActionPlan("GatherWood", ["A", "B"], [new ActionData { Tool = "T1" }]);
        var str = plan.ToString();
        Assert.That(str, Does.Contain("GatherWood"));
        Assert.That(str, Does.Contain("1 actions"));
    }
}
