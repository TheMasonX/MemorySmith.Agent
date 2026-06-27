using Agent.Construction;
using Agent.Core;
using Agent.Planning;
using Agent.Planning.Goals;

namespace MemorySmith.Agent.Tests;

/// <summary>
/// Tests for <see cref="HtnPlanner"/> Phase 4b: BuildGoal decomposition produces
/// the correct mix of MineBlock (material gathering) and PlaceBlock (construction)
/// actions, and applies the build origin from world-state facts.
///
/// Sprint 13 D3: HtnPlanner passes requireOrigin:true to DecomposeBuild, so tests
/// that use MakePlan must ensure the state has a resolvable origin. MakePlan now
/// sets BuildFactKeys.AutoOrigin{X,Y,Z} defaults (x=0, y=64, z=0) so the auto-origin
/// resolver finds a valid, non-zero-Y value and the full build plan is generated.
/// Tests that explicitly set blueprint-specific origin facts override this default.
/// </summary>
[TestFixture]
[Description("HtnPlanner BuildGoal: PlaceBlock actions, material gathering, origin offset")]
public sealed class HtnPlannerBuildTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<PlacementBlock> SmallFloor =
    [
        new(0, 0, 0, "cobblestone"),
        new(1, 0, 0, "cobblestone"),
        new(2, 0, 0, "cobblestone"),
        new(0, 0, 1, "cobblestone"),
        new(1, 0, 1, "cobblestone"),
        new(2, 0, 1, "cobblestone"),
    ];

    private static Blueprint MakeBlueprintWithMaterials(
        string id = "test-house",
        string name = "Test House",
        MaterialEntry[]? materials = null) => new()
    {
        Id        = id,
        Name      = name,
        Materials = materials ?? [new MaterialEntry("cobblestone", 6)],
    };

    // ── PlaceBlock actions ────────────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_BuildGoal_ProducesSixPlaceBlockActions()
    {
        // All 6 PlaceBlock actions flow through to the adapter. The adapter
        // handles bot-position skip (Sprint 51) via BlockPlaceSkippedEvent,
        // which triggers AdvanceBuildCheckpoint on the C# side.
        var plan = await MakePlan(SmallFloor, new WorldState());
        Assert.That(CountTool(plan, "place"), Is.EqualTo(6));
    }

    [Test]
    public async Task PlanAsync_BuildGoal_PlaceBlocks_HaveCorrectMaterial()
    {
        var plan  = await MakePlan(SmallFloor, new WorldState());
        var place = plan.Actions.Where(a => a.Tool == "place").ToList();
        Assert.That(place, Has.All.Matches<ActionData>(a =>
            a.Arguments.TryGetValue("material", out var m) &&
            m?.ToString() == "cobblestone"));
    }

    [Test]
    public async Task PlanAsync_BuildGoal_PlaceBlocks_HaveXYZArguments()
    {
        var plan  = await MakePlan(SmallFloor, new WorldState());
        var place = plan.Actions.Where(a => a.Tool == "place").ToList();
        Assert.That(place, Has.All.Matches<ActionData>(a =>
            a.Arguments.ContainsKey("x") &&
            a.Arguments.ContainsKey("y") &&
            a.Arguments.ContainsKey("z")));
    }

    // ── Build origin applied ──────────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_BuildGoal_OriginX_AddedToBlockX()
    {
        var state = new WorldState().With(b =>
        {
            b.SetFact("build:offset-test:origin:x", 100.ToString(), FactSource.Observed);
            b.SetFact("build:offset-test:origin:y", 64.ToString(), FactSource.Observed);
            b.SetFact("build:offset-test:origin:z", 200.ToString(), FactSource.Observed);
        });

        var plan   = await MakePlan(SmallFloor, state, "offset-test");
        var place  = plan.Actions.Where(a => a.Tool == "PlaceBlock").ToList();

        // All X coords should be 100 or 101 or 102 (100 + blueprint X 0-2)
        Assert.That(place, Has.All.Matches<ActionData>(a =>
        {
            var xVal = a.Arguments["x"];
            var x    = xVal is int xi ? xi : xVal is long xl ? (int)xl : 0;
            return x >= 100 && x <= 102;
        }));
    }

    [Test]
    public async Task PlanAsync_BuildGoal_OriginY_AddedToBlockY()
    {
        var state = new WorldState().With(b =>
        {
            b.SetFact("build:y-test:origin:x", 0.ToString(), FactSource.Observed);
            b.SetFact("build:y-test:origin:y", 64.ToString(), FactSource.Observed);
            b.SetFact("build:y-test:origin:z", 0.ToString(), FactSource.Observed);
        });

        var plan  = await MakePlan(SmallFloor, state, "y-test");
        var place = plan.Actions.Where(a => a.Tool == "PlaceBlock").ToList();

        Assert.That(place, Has.All.Matches<ActionData>(a =>
        {
            var yVal = a.Arguments["y"];
            var y    = yVal is int yi ? yi : yVal is long yl ? (int)yl : 0;
            return y == 64; // all floor blocks at origin Y = 64
        }));
    }

    // ── Material gathering ────────────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_BuildGoal_EmitsMineBlock_WhenCobblestoneNotInInventory()
    {
        // Inventory empty — cobblestone must be mined
        var plan = await MakePlan(SmallFloor, new WorldState());
        Assert.That(CountTool(plan, "MineBlock"), Is.GreaterThan(0));
    }

    [Test]
    public async Task PlanAsync_BuildGoal_SkipsMining_WhenInventorySufficient()
    {
        // Inventory already has 10 cobblestone — blueprint only needs 6
        var state = new WorldState().With(b => b.AddInventoryItem("cobblestone", 10));
        var plan  = await MakePlan(SmallFloor, state);
        Assert.That(CountTool(plan, "MineBlock"), Is.EqualTo(0));
    }

    [Test]
    public async Task PlanAsync_BuildGoal_SkipsMining_WhenCreativeModeEnabled()
    {
        var state = new WorldState().With(b => b.SetFact("world:gamemode", "creative", FactSource.Observed));
        var plan  = await MakePlan(SmallFloor, state);

        Assert.That(CountTool(plan, "MineBlock"), Is.EqualTo(0),
            "Creative mode should skip material gathering for build plans.");
    }

    [Test]
    public async Task PlanAsync_BuildGoal_DoesNotMine_NonMineableBlocks()
    {
        // oak_planks is crafted — should never emit a MineBlock("oak_planks") action.
        // Sprint 13 D3: provide explicit origin so the full build plan is generated.
        var blueprint = MakeBlueprintWithMaterials(
            materials: [new MaterialEntry("oak_planks", 64)]);
        var blocks = new[] { new PlacementBlock(0, 1, 0, "oak_planks") };
        var goal   = new BuildGoal(blueprint, blocks);
        var state  = WithAutoOrigin(new WorldState());

        var plan    = await new HtnPlanner(new HtnTaskLibrary()).PlanAsync(goal, state);
        var mining  = plan.Actions.Where(a => a.Tool == "MineBlock").ToList();

        Assert.That(mining, Is.Empty,
            "oak_planks is crafted — DecomposeBuild must not emit MineBlock for it");
    }

    // ── Plan metadata ─────────────────────────────────────────────────────────

    [Test]
    public async Task PlanAsync_BuildGoal_GoalNameIsPreserved()
    {
        var plan = await MakePlan(SmallFloor, new WorldState(), "named-house");
        Assert.That(plan.GoalName, Is.EqualTo("Build:named-house"));
    }

    [Test]
    public async Task PlanAsync_BuildGoal_PlanIsNotEmpty()
    {
        var plan = await MakePlan(SmallFloor, new WorldState());
        Assert.That(plan.Actions, Is.Not.Empty);
    }

    [Test]
    public async Task PlanAsync_BuildGoal_PlanEndsWithGetStatus()
    {
        var plan = await MakePlan(SmallFloor, new WorldState());
        Assert.That(plan.Actions.Last().Tool, Is.EqualTo("GetStatus"));
    }

    // ── Helper methods ────────────────────────────────────────────────────────

    private static async Task<IPlan> MakePlan(
        IReadOnlyList<PlacementBlock> blocks,
        WorldState state,
        string blueprintId = "test-house")
    {
        var library = new HtnTaskLibrary();
        var planner = new HtnPlanner(library);
        var bp      = MakeBlueprintWithMaterials(id: blueprintId);
        var goal    = new BuildGoal(bp, blocks);
        // Sprint 13 D3: HtnPlanner passes requireOrigin:true; ensure a valid origin
        // (y=64) is set via auto-origin facts so the full plan is generated.
        // Tests that set explicit blueprint origin facts will use those instead.
        return await planner.PlanAsync(goal, WithAutoOrigin(state));
    }

    /// <summary>
    /// Adds auto-origin defaults (x=0, y=64, z=0) to a WorldState so that
    /// DecomposeBuild's requireOrigin check passes when no explicit origin is set.
    /// y=64 is a typical overworld surface height — non-zero, so (0,64,0) != (0,0,0).
    /// </summary>
    private static WorldState WithAutoOrigin(WorldState state) =>
        state.With(b =>
        {
            b.SetFact(BuildFactKeys.AutoOriginX, 0.ToString(), FactSource.Observed);
            b.SetFact(BuildFactKeys.AutoOriginY, 64.ToString(), FactSource.Observed);
            b.SetFact(BuildFactKeys.AutoOriginZ, 0.ToString(), FactSource.Observed);
        });

    private static int CountTool(IPlan plan, string toolName) =>
        plan.Actions.Count(a =>
            string.Equals(a.Tool, toolName, StringComparison.OrdinalIgnoreCase));
}
