using Agent.Core;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests for SurviveNightGoal — previously untested (Finding 7 from architecture review).
/// </summary>
[TestFixture]
public class SurviveNightGoalTests
{
    private SurviveNightGoal _goal = null!;

    [SetUp]
    public void SetUp() => _goal = new SurviveNightGoal();

    [Test]
    public void Name_IsSurviveNight()     => Assert.That(_goal.Name, Is.EqualTo("SurviveNight"));
    [Test]
    public void Description_IsNonEmpty() => Assert.That(_goal.Description, Is.Not.Empty);
    [Test]
    public void Phases_HasThreePhases()  => Assert.That(_goal.Phases, Has.Length.EqualTo(3));

    // ── HasFailed ─────────────────────────────────────────────────────────────

    [Test]
    public void HasFailed_HealthAboveThreshold_IsFalse()
    {
        // CriticalHealthThreshold = 4; health 5 is safe
        var state = new WorldState().With(b => b.SetHealth(5));
        Assert.That(_goal.HasFailed(state), Is.False);
    }

    [Test]
    public void HasFailed_HealthAtThreshold_IsTrue()
    {
        var state = new WorldState().With(b => b.SetHealth(4));
        Assert.That(_goal.HasFailed(state), Is.True);
    }

    [Test]
    public void HasFailed_HealthBelowThreshold_IsTrue()
    {
        var state = new WorldState().With(b => b.SetHealth(0));
        Assert.That(_goal.HasFailed(state), Is.True);
    }

    // ── IsComplete ────────────────────────────────────────────────────────────

    [Test]
    public void IsComplete_NoFacts_IsFalse()
    {
        Assert.That(_goal.IsComplete(new WorldState()), Is.False);
    }

    [Test]
    public void IsComplete_TimeOfDay_Day_IsTrue()
    {
        var state = new WorldState().With(b => b.SetFact("timeOfDay", "day"));
        Assert.That(_goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_TimeOfDay_Morning_IsTrue()
    {
        var state = new WorldState().With(b => b.SetFact("timeOfDay", "morning"));
        Assert.That(_goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_TimeOfDay_Night_IsFalse()
    {
        var state = new WorldState().With(b => b.SetFact("timeOfDay", "night"));
        Assert.That(_goal.IsComplete(state), Is.False);
    }

    [Test]
    public void IsComplete_InShelter_IsTrue()
    {
        var state = new WorldState().With(b => b.SetFact("inShelter", (object?)true));
        Assert.That(_goal.IsComplete(state), Is.True);
    }

    [Test]
    public void IsComplete_InShelter_False_IsFalse()
    {
        var state = new WorldState().With(b => b.SetFact("inShelter", (object?)false));
        Assert.That(_goal.IsComplete(state), Is.False);
    }
}
