using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

[TestFixture]
public class HtnPlannerTests
{
    private HtnTaskLibrary _library = null!;
    private HtnPlanner _planner = null!;

    [SetUp]
    public void SetUp()
    {
        _library = new HtnTaskLibrary();
        _planner = new HtnPlanner(_library);
    }

    // ── GatherWoodGoal ────────────────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_GatherWoodGoal_ReturnsNonEmptyPlan()
    {
        var goal = new GatherWoodGoal(10);
        var plan = await _planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.GoalName, Is.EqualTo("GatherWood"));
        Assert.That(plan.Actions, Is.Not.Empty);
    }

    [Test]
    public async Task PlanAsync_GatherWoodGoal_ContainsMineBlockAction()
    {
        var goal = new GatherWoodGoal(10);
        var plan = await _planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.Actions.Any(a => a.Tool == "MineBlock"), Is.True,
            "Plan should contain a MineBlock tool call.");
        // Sprint 44 (TSK-0080): SearchMemory was removed from gather plan.
        Assert.That(plan.Actions.Any(a => a.Tool == "SearchMemory"), Is.False,
            "Sprint 44: SearchMemory removed from gather plan (results never consumed).");
    }

    // ── SurviveNightGoal ──────────────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_SurviveNightGoal_ReturnsNonEmptyPlan()
    {
        var goal = new SurviveNightGoal();
        var plan = await _planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.GoalName, Is.EqualTo("SurviveNight"));
        Assert.That(plan.Actions, Is.Not.Empty);
    }

    // ── Phase-based decomposition ─────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_SimpleGoalWithKnownPhases_DecomposesPhases()
    {
        // A goal with known phases but no direct task entry
        var goal = new SimpleGoal("CustomGoal", "", ["FindTree", "MineWood"], _ => false);
        var plan = await _planner.PlanAsync(goal, new WorldState());

        Assert.That(plan.GoalName, Is.EqualTo("CustomGoal"));
        Assert.That(plan.Actions, Is.Not.Empty);
    }

    [Test]
    public void PlanAsync_UnknownGoalNoPhases_ThrowsInvalidOperation()
    {
        var goal = new SimpleGoal("AcquireDiamond", "", [], _ => false);
        Assert.ThrowsAsync<InvalidOperationException>(
            () => _planner.PlanAsync(goal, new WorldState()));
    }

    // ── ReplanAsync ───────────────────────────────────────────────────────────

    [Test]
    public async Task ReplanAsync_KnownGoal_ReturnsNewPlan()
    {
        var goal = new GatherWoodGoal(5);
        var originalPlan = await _planner.PlanAsync(goal, new WorldState());

        var replan = await _planner.ReplanAsync(
            new ReplanGoalContext(originalPlan, new WorldState(), "path blocked"));

        Assert.That(replan.IsSuccess, Is.True);
        Assert.That(replan.Plan!.GoalName, Is.EqualTo("GatherWood"));
    }

    [Test]
    public async Task ReplanAsync_UnknownGoal_ReturnsNull()
    {
        // An action plan for an unknown goal
        var plan = new ActionPlan("NoSuchGoal", [], [new ActionData { Tool = "GetStatus" }]);
        var result = await _planner.ReplanAsync(
            new ReplanGoalContext(plan, new WorldState(), "failure"));
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Plan, Is.Null);
    }

    // ── HtnTaskLibrary ────────────────────────────────────────────────────────

    [Test]
    public void Library_HasGatherWood()
        => Assert.That(_library.HasTask("GatherWood"), Is.True);

    [Test]
    public void Library_HasSurviveNight()
        => Assert.That(_library.HasTask("SurviveNight"), Is.True);

    [Test]
    public void Library_MissingTask_ThrowsOnDecompose()
    {
        Assert.Throws<InvalidOperationException>(
            () => _library.Decompose("NoSuchTask", [], new WorldState()));
    }
}
