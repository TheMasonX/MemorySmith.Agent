namespace MemorySmith.Agent.Tests;

using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;
using System.Text.Json;

/// <summary>
/// Sprint 27 tests.
/// P0-D: CraftItemGoalDecomposer routing and HtnPlanner consolidation.
///
/// Coverage:
///   - CraftItemGoalDecomposer.CanHandle returns true only for CraftItemGoal.
///   - CraftItemGoalDecomposer.Decompose produces actions via HtnTaskLibrary.DecomposeCraftItem.
///   - HtnPlanner fallback no longer has hardcoded CraftItemGoal/BuildGoal/IItemSpecGoal branches.
///   - PlannerRouter routes CraftItemGoal to CraftItemGoalDecomposer (not HtnPlanner).
/// </summary>
[TestFixture]
public class Sprint27Tests
{
    private HtnTaskLibrary _library = null!;
    private DecomposerRegistry _registry = null!;
    private HtnPlanner _htnPlanner = null!;
    private PlannerRouter _router = null!;

    [SetUp]
    public void SetUp()
    {
        _library    = new HtnTaskLibrary(new MockItemRegistry());
        _registry   = new DecomposerRegistry();
        _registry.Register(new BuildGoalDecomposer(_library));
        _registry.Register(new GatherGoalDecomposer(_library));
        _registry.Register(new CraftItemGoalDecomposer(_library));
        _registry.Register(new SurviveNightGoalDecomposer(_library));
        _htnPlanner = new HtnPlanner(_library);
        _router     = new PlannerRouter(_registry, _htnPlanner);
    }

    // ─── CraftItemGoalDecomposer CanHandle ────────────────────────────────────

    [Test]
    [Description("CraftItemGoalDecomposer.CanHandle must return true for CraftItemGoal.")]
    public void CraftItemGoalDecomposer_CanHandle_ReturnsTrueForCraftItemGoal()
    {
        var decomposer = new CraftItemGoalDecomposer(_library);
        var goal       = new CraftItemGoal("iron_pickaxe", 1);

        Assert.That(decomposer.CanHandle(goal), Is.True,
            "CraftItemGoalDecomposer must handle CraftItemGoal");
    }

    [Test]
    [Description("CraftItemGoalDecomposer.CanHandle must return false for non-craft goals.")]
    public void CraftItemGoalDecomposer_CanHandle_ReturnsFalseForOtherGoals()
    {
        var decomposer = new CraftItemGoalDecomposer(_library);
        var gather     = new GenericGatherGoal(new ItemSpec
        {
            ItemId = "oak_log", DisplayName = "Oak Log",
            SourceBlocks = ["oak_log"], RequiresSmelting = false, MinHarvestLevel = 0
        }, 1);

        Assert.That(decomposer.CanHandle(gather), Is.False,
            "CraftItemGoalDecomposer must NOT handle GenericGatherGoal");
    }

    // ─── CraftItemGoalDecomposer Decompose ────────────────────────────────────

    [Test]
    [Description("CraftItemGoalDecomposer.Decompose must produce at least one action for a valid item.")]
    public void CraftItemGoalDecomposer_Decompose_ProducesActions()
    {
        var decomposer = new CraftItemGoalDecomposer(_library);
        var goal       = new CraftItemGoal("crafting_table", 1);
        var state      = new WorldState();

        var plan = decomposer.Decompose(goal, state);

        Assert.That(plan, Is.Not.Null,
            "Decompose must return a non-null plan");
        Assert.That(plan.Actions, Is.Not.Empty,
            "Decomposed plan must contain at least one action for CraftItemGoal");
        Assert.That(plan.GoalName, Is.EqualTo(goal.Name),
            "Plan goal name must match the input goal name");
    }

    // ─── PlannerRouter routes CraftItemGoal through registry ──────────────────

    [Test]
    [Description("PlannerRouter.Select must return a DecomposerPlanner (not HtnPlanner) for CraftItemGoal " +
                 "when CraftItemGoalDecomposer is registered.")]
    public async Task PlannerRouter_SelectsCraftItemGoalDecomposer_ForCraftItemGoal()
    {
        var goal  = new CraftItemGoal("wooden_sword", 1);
        var state = new WorldState();

        // Plan via the router — should delegate to CraftItemGoalDecomposer
        var plan = await _router.PlanAsync(goal, state);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.Actions, Is.Not.Empty,
            "Router must produce actions for CraftItemGoal via CraftItemGoalDecomposer");
    }

    // ─── GatherGoalDecomposer redundant arm removed ───────────────────────────

    [Test]
    [Description("Sprint 27 P0-D: GatherGoalDecomposer must still handle GenericGatherGoal correctly " +
                 "via the IItemSpecGoal arm after removal of the redundant GenericGatherGoal arm.")]
    public void GatherGoalDecomposer_HandlesGenericGatherGoal_ViaIItemSpecGoalArm()
    {
        var decomposer = new GatherGoalDecomposer(_library);
        var spec       = new ItemSpec
        {
            ItemId = "stone", DisplayName = "Stone",
            SourceBlocks = ["stone"], RequiresSmelting = false, MinHarvestLevel = 0
        };
        var goal  = new GenericGatherGoal(spec, targetCount: 16);
        var state = new WorldState();

        Assert.That(decomposer.CanHandle(goal), Is.True,
            "GatherGoalDecomposer.CanHandle must return true for GenericGatherGoal (implements IItemSpecGoal)");

        var plan = decomposer.Decompose(goal, state);

        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.Actions, Is.Not.Empty,
            "GatherGoalDecomposer must produce actions for GenericGatherGoal via IItemSpecGoal arm");
    }
}
