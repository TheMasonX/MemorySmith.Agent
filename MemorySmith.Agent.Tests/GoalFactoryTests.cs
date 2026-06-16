using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests for GoalFactory — the name-to-goal registry used by the REST endpoint
/// POST /api/agent/plan to resolve named goals from request parameters.
/// Previously untested (Finding 7 from architecture review).
/// </summary>
[TestFixture]
public class GoalFactoryTests
{
    private GoalFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new GoalFactory();

    // ── Registration ──────────────────────────────────────────────────────────

    [Test]
    public void RegisteredGoals_ContainsAllKnownGoals()
    {
        Assert.That(_factory.RegisteredGoals, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(_factory.RegisteredGoals, Contains.Item("GatherWood"));
        Assert.That(_factory.RegisteredGoals, Contains.Item("SurviveNight"));
    }

    // ── GatherWood ────────────────────────────────────────────────────────────

    [Test]
    public void Create_GatherWood_NoParams_DefaultsToTen()
    {
        var goal = _factory.Create("GatherWood");
        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.Name, Is.EqualTo("GatherWood"));

        // Default count = 10 — goal should NOT be complete with 9 logs
        var state9 = new WorldState().With(b => b.AddInventoryItem("oak_log", 9));
        Assert.That(goal.IsComplete(state9), Is.False);

        // Should be complete with 10
        var state10 = new WorldState().With(b => b.AddInventoryItem("oak_log", 10));
        Assert.That(goal.IsComplete(state10), Is.True);
    }

    [Test]
    public void Create_GatherWood_WithIntCount_UsesCount()
    {
        var goal = _factory.Create("GatherWood",
            new Dictionary<string, object?> { ["count"] = 3 });
        Assert.That(goal, Is.Not.Null);

        var state2 = new WorldState().With(b => b.AddInventoryItem("oak_log", 2));
        Assert.That(goal!.IsComplete(state2), Is.False);

        var state3 = new WorldState().With(b => b.AddInventoryItem("oak_log", 3));
        Assert.That(goal.IsComplete(state3), Is.True);
    }

    [Test]
    public void Create_GatherWood_WithStringCount_ParsesCorrectly()
    {
        var goal = _factory.Create("GatherWood",
            new Dictionary<string, object?> { ["count"] = "5" });
        Assert.That(goal, Is.Not.Null);

        var state5 = new WorldState().With(b => b.AddInventoryItem("oak_log", 5));
        Assert.That(goal!.IsComplete(state5), Is.True);
    }

    // ── SurviveNight ──────────────────────────────────────────────────────────

    [Test]
    public void Create_SurviveNight_ReturnsGoal()
    {
        var goal = _factory.Create("SurviveNight");
        Assert.That(goal, Is.Not.Null);
        Assert.That(goal!.Name, Is.EqualTo("SurviveNight"));
    }

    // ── Unknown goals ─────────────────────────────────────────────────────────

    [Test]
    public void Create_UnknownGoal_ReturnsNull()
    {
        var goal = _factory.Create("BuildCastle");
        Assert.That(goal, Is.Null);
    }

    [Test]
    public void Create_CaseInsensitive_FindsGoal()
    {
        // Goal registration is case-insensitive per StringComparer.OrdinalIgnoreCase
        var goal = _factory.Create("gatherwood");
        Assert.That(goal, Is.Not.Null);
    }

    [Test]
    public void Create_NullParameters_UsesDefaults()
    {
        var goal = _factory.Create("GatherWood", null);
        Assert.That(goal, Is.Not.Null);
    }
}
